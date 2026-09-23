using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure;

/// <summary>
/// R08 / A09: auditoría en MongoDB. Billing solo inserta en <c>eventos</c> y lee <c>eventos</c> e
/// <c>identity_events</c> de su tenant (rol <c>billingAudit</c>: insert y find, sin update ni delete).
/// </summary>
public sealed partial class MongoAuditoria(IMongoDatabase database, ILogger<MongoAuditoria> logger) : IAuditoria
{
    private readonly IMongoCollection<BsonDocument> _billing = database.GetCollection<BsonDocument>("eventos");
    private readonly IMongoCollection<BsonDocument> _identity = database.GetCollection<BsonDocument>("identity_events");

    public async Task Registrar(EventoAuditoria evento, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evento);
        BsonDocument documento = new()
        {
            ["timestamp"] = evento.Timestamp.UtcDateTime,
            ["tenant_id"] = evento.TenantId.ToString("D"),
            ["actor"] = evento.Actor is Guid actor ? actor.ToString("D") : BsonNull.Value,
            ["accion"] = evento.Accion,
            ["recurso"] = (BsonValue?)evento.Recurso ?? BsonNull.Value,
            ["resultado"] = Resultado(evento.Resultado),
            ["ip"] = (BsonValue?)evento.Ip ?? BsonNull.Value,
            ["correlationId"] = (BsonValue?)evento.CorrelationId ?? BsonNull.Value,
            ["detalle"] = (BsonValue?)evento.Detalle ?? BsonNull.Value,
        };

        try
        {
            await _billing.InsertOneAsync(documento, cancellationToken: cancellationToken);
        }
        // La operación de negocio ya se confirmó en Postgres: un fallo de Mongo no la convierte en 500 (R18). Sin un
        // outbox transaccional puede quedar un evento sin registrar (limitación documentada en el README).
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            AuditoriaNoDisponible(logger, evento.Accion, ex);
        }
    }

    public async Task<Pagina<EventoAuditoriaDto>> Consultar(Guid tenantId, FiltroAuditoria filtro, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        int necesarios = filtro.Pagina * filtro.Tamano;
        FilterDefinition<BsonDocument> billing = FiltroBilling(tenantId, filtro);
        FilterDefinition<BsonDocument>? identity = FiltroIdentity(tenantId, filtro);

        long total = await _billing.CountDocumentsAsync(billing, cancellationToken: cancellationToken);
        List<EventoAuditoriaDto> eventos = (await _billing.Find(billing).SortByDescending(x => x["timestamp"])
            .Limit(necesarios).ToListAsync(cancellationToken)).Select(DesdeBilling).ToList();

        if (identity is not null)
        {
            total += await _identity.CountDocumentsAsync(identity, cancellationToken: cancellationToken);
            eventos.AddRange((await _identity.Find(identity).SortByDescending(x => x["timestamp"])
                .Limit(necesarios).ToListAsync(cancellationToken)).Select(DesdeIdentity));
        }

        EventoAuditoriaDto[] pagina = eventos.OrderByDescending(x => x.Timestamp).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano).ToArray();
        return new Pagina<EventoAuditoriaDto>(pagina, (int)Math.Min(total, int.MaxValue), filtro.Pagina, filtro.Tamano);
    }

    private static FilterDefinition<BsonDocument> FiltroBilling(Guid tenantId, FiltroAuditoria filtro)
    {
        FilterDefinitionBuilder<BsonDocument> f = Builders<BsonDocument>.Filter;
        List<FilterDefinition<BsonDocument>> partes = [f.Eq("tenant_id", tenantId.ToString("D"))];
        if (!string.IsNullOrEmpty(filtro.Accion))
        {
            partes.Add(f.Eq("accion", filtro.Accion));
        }

        if (filtro.Resultado is ResultadoAuditoria resultado)
        {
            partes.Add(f.Eq("resultado", Resultado(resultado)));
        }

        partes.AddRange(Fechas(filtro));
        return f.And(partes);
    }

    // Identity registra login, lockout, registro y logout con success true/false: se muestran como Exito o Fallido.
    private static FilterDefinition<BsonDocument>? FiltroIdentity(Guid tenantId, FiltroAuditoria filtro)
    {
        if (filtro.Resultado is ResultadoAuditoria.Denegado)
        {
            return null;
        }

        FilterDefinitionBuilder<BsonDocument> f = Builders<BsonDocument>.Filter;
        List<FilterDefinition<BsonDocument>> partes = [f.Eq("tenantId", new BsonBinaryData(tenantId, GuidRepresentation.Standard))];
        if (!string.IsNullOrEmpty(filtro.Accion))
        {
            partes.Add(f.Eq("action", filtro.Accion));
        }

        if (filtro.Resultado is ResultadoAuditoria resultado)
        {
            partes.Add(f.Eq("success", resultado == ResultadoAuditoria.Exito));
        }

        partes.AddRange(Fechas(filtro));
        return f.And(partes);
    }

    private static IEnumerable<FilterDefinition<BsonDocument>> Fechas(FiltroAuditoria filtro)
    {
        if (filtro.Desde is DateOnly desde)
        {
            yield return Builders<BsonDocument>.Filter.Gte("timestamp", desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }

        if (filtro.Hasta is DateOnly hasta)
        {
            yield return Builders<BsonDocument>.Filter.Lt("timestamp", hasta.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }
    }

    private static EventoAuditoriaDto DesdeBilling(BsonDocument d) => new(
        d["_id"].ToString()!,
        new DateTimeOffset(d["timestamp"].ToUniversalTime(), TimeSpan.Zero),
        "billing",
        Guid.TryParse(Texto(d, "actor"), out Guid actor) ? actor : null,
        Texto(d, "accion") ?? "",
        Texto(d, "recurso"),
        Texto(d, "resultado") switch
        {
            "denegado" => ResultadoAuditoria.Denegado,
            "fallido" => ResultadoAuditoria.Fallido,
            _ => ResultadoAuditoria.Exito,
        },
        Texto(d, "ip"),
        Texto(d, "correlationId"),
        Texto(d, "detalle"));

    private static EventoAuditoriaDto DesdeIdentity(BsonDocument d) => new(
        d["_id"].ToString()!,
        new DateTimeOffset(d["timestamp"].ToUniversalTime(), TimeSpan.Zero),
        "identity",
        d.GetValue("actor", BsonNull.Value) is BsonBinaryData actor ? actor.ToGuid() : null,
        Texto(d, "action") ?? "",
        null,
        d.GetValue("success", false).ToBoolean() ? ResultadoAuditoria.Exito : ResultadoAuditoria.Fallido,
        Texto(d, "ip"),
        Texto(d, "correlationId"),
        Texto(d, "detail"));

    private static string? Texto(BsonDocument documento, string campo) =>
        documento.GetValue(campo, BsonNull.Value) is { IsString: true } valor ? valor.AsString : null;

    private static string Resultado(ResultadoAuditoria resultado) => resultado switch
    {
        ResultadoAuditoria.Denegado => "denegado",
        ResultadoAuditoria.Fallido => "fallido",
        _ => "exito",
    };

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "No se pudo registrar el evento de auditoría {Accion} de Billing.")]
    private static partial void AuditoriaNoDisponible(ILogger logger, string accion, Exception exception);
}
