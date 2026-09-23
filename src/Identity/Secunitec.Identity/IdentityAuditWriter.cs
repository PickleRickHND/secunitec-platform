// R08 / A09: los eventos de autenticación (login, lockout, registro) se guardan en Mongo, solo inserción.
// La escritura nunca interrumpe el login: si Mongo falla queda un warning en el log. Cada evento también va al log
// (Information) sin datos personales, para verlo en Loki junto a su traza.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Secunitec.BuildingBlocks.AspNetCore.Http;

namespace Secunitec.Identity;

public sealed partial class IdentityAuditWriter
{
    private readonly IMongoCollection<IdentityAuditEvent> _events;
    private readonly ILogger<IdentityAuditWriter> _logger;

    public IdentityAuditWriter(IMongoDatabase database, ILogger<IdentityAuditWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        _logger = logger;
        _events = database.GetCollection<IdentityAuditEvent>("identity_events");
    }

    /// <summary>Registra el evento con la IP real del cliente y el correlation id de la petición.</summary>
    public async Task WriteAsync(
        HttpContext context,
        string action,
        bool success,
        Guid? actor,
        Guid? tenantId,
        string? detail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Fase 4: el evento también va al log (Loki), enlazado a su traza. TB3: sin actor, IP ni detalle.
        EventRecorded(_logger, action, success ? "exito" : "fallido");
        try
        {
            await _events.InsertOneAsync(
                new IdentityAuditEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = action,
                    Success = success,
                    Actor = actor,
                    TenantId = tenantId,
                    // UseForwardedHeaders ya reemplazó la IP del gateway por la del cliente (X-Forwarded-For).
                    Ip = context.Connection.RemoteIpAddress?.ToString(),
                    CorrelationId = context.GetCorrelationId() ?? context.TraceIdentifier,
                    Detail = detail,
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or MongoDB.Bson.BsonException)
        {
            AuditWriteFailed(_logger, action, ex);
        }
    }

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "Auditoría: {Action} ({Result}).")]
    private static partial void EventRecorded(ILogger logger, string action, string result);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning,
        Message = "No se pudo registrar el evento de auditoría {Action} de Identity.")]
    private static partial void AuditWriteFailed(ILogger logger, string action, Exception exception);
}

/// <summary>Evento de auditoría; los nombres de campo coinciden con los índices de infra/mongo/init/02-identity.js.</summary>
public sealed class IdentityAuditEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Fecha BSON (UTC): el serializador por defecto de DateTimeOffset guarda un documento, que Mongo no puede ordenar
    // ni filtrar por rango (la pantalla de auditoría de Billing lee estos eventos).
    [BsonElement("timestamp")]
    [BsonRepresentation(BsonType.DateTime)]
    public DateTimeOffset Timestamp { get; set; }

    [BsonElement("action")]
    public string Action { get; set; } = "";

    [BsonElement("success")]
    public bool Success { get; set; }

    // MongoDB.Driver 3 no serializa Guid sin una representación explícita: sin esto, ningún evento se guardaba.
    [BsonElement("actor")]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? Actor { get; set; }

    [BsonElement("tenantId")]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? TenantId { get; set; }

    [BsonElement("ip")]
    public string? Ip { get; set; }

    [BsonElement("correlationId")]
    public string CorrelationId { get; set; } = "";

    [BsonElement("detail")]
    public string? Detail { get; set; }
}
