using MongoDB.Bson;
using MongoDB.Driver;
using Secunitec.Billing.Application;

namespace Secunitec.Billing.Infrastructure;

public sealed class MongoBillingAudit(IMongoDatabase database) : IBillingAudit
{
    public async Task Registrar(Guid tenantId, Guid actor, string accion, Guid recurso, CancellationToken cancellationToken)
    {
        BsonDocument evento = new()
        {
            ["timestamp"] = DateTime.UtcNow,
            ["tenant_id"] = tenantId.ToString("D"),
            ["actor"] = actor.ToString("D"),
            ["accion"] = accion,
            ["recurso"] = recurso.ToString("D")
        };
        await database.GetCollection<BsonDocument>("eventos").InsertOneAsync(evento, cancellationToken: cancellationToken);
    }
}
