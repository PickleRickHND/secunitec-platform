using Microsoft.EntityFrameworkCore;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure;

/// <summary>
/// Datos de demostración para Development (H2): un obligado con CAI vigente y un cliente, en el tenant de
/// ejemplo. Identity siembra los usuarios por rol; el usuario Cliente apunta a este cliente. Es idempotente.
/// Corre sin usuario: ignora el filtro de tenant (con él no vería nada y reinsertaría en cada arranque).
/// </summary>
public static class BillingDemoSeeder
{
    public static async Task SeedAsync(BillingDbContext db, Guid tenantId, Guid clienteId, DateOnly hoy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!await db.Obligados.IgnoreQueryFilters().AnyAsync(x => x.Id == tenantId, cancellationToken))
        {
            db.Obligados.Add(new ObligadoTributario(tenantId, new Rtn("08019000000001"), "Secunitec Demo S. de R.L.",
                new Cai("A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6"), "000-001-01", 1, 1_000_000, hoy.AddYears(1)));
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.Clientes.IgnoreQueryFilters().AnyAsync(x => x.Id == clienteId, cancellationToken))
        {
            db.Clientes.Add(new Cliente(clienteId, tenantId, "Cliente de demostración", null, "cliente@secunitec.local"));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
