// R08: los eventos de autenticaciÃ³n se persisten como auditorÃ­a en MongoDB.
// La escritura de auditorÃ­a permanece separada del flujo de emisiÃ³n de tokens.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Secunitec.Identity;

public sealed partial class IdentityAuditWriter
{
    private readonly IMongoCollection<IdentityAuditEvent> _events;
    private readonly ILogger<IdentityAuditWriter> _logger;

    public IdentityAuditWriter(
        IMongoDatabase database,
        ILogger<IdentityAuditWriter> logger)
    {
        _logger = logger;
        _events = database.GetCollection<IdentityAuditEvent>("identity_events");
    }

    public async Task WriteAsync(
        string action,
        bool success,
        Guid? actor,
        Guid? tenantId,
        string? ip,
        string correlationId,
        string? detail,
        CancellationToken cancellationToken)
    {
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
                    Ip = ip,
                    CorrelationId = correlationId,
                    Detail = detail
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            AuditWriteFailed(_logger, ex);
        }
    }

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Warning,
        Message = "No se pudo registrar auditorÃ­a de Identity.")]
    private static partial void AuditWriteFailed(
        ILogger logger,
        Exception exception);
}

public sealed class IdentityAuditEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }
    public string Action { get; set; } = "";
    public bool Success { get; set; }
    public Guid? Actor { get; set; }
    public Guid? TenantId { get; set; }
    public string? Ip { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? Detail { get; set; }
}