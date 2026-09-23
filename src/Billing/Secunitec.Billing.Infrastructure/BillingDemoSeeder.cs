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
    public static Task SeedAsync(BillingDbContext db, Guid tenantId, Guid clienteId, DateOnly hoy, CancellationToken cancellationToken) =>
        EnsureAsync(db, tenantId, new Rtn("08019000000001"), "Secunitec Demo S. de R.L.", "A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6",
            clienteId, "Cliente de demostración", "cliente@secunitec.local", hoy, cancellationToken);

    /// <summary>
    /// Etapa 5.3: tenant de carga de los clientes <c>jmeter-load-NN</c>, para que las pruebas de estrés no llenen el
    /// tenant de ejemplo (el que ven el SPA y la demo) con miles de borradores. RTN propio: el RTN es único.
    /// </summary>
    public static Task SeedLoadTenantAsync(BillingDbContext db, Guid tenantId, Guid clienteId, DateOnly hoy, CancellationToken cancellationToken) =>
        EnsureAsync(db, tenantId, new Rtn("08019000000002"), "Secunitec Carga S. de R.L.", "B1C2D3-E4F5A6-B7C8D9-E0F1A2-B3C4D5-E6",
            clienteId, "Cliente de pruebas de carga", "carga@secunitec.local", hoy, cancellationToken);

    private static async Task EnsureAsync(
        BillingDbContext db, Guid tenantId, Rtn rtn, string razonSocial, string cai, Guid clienteId, string clienteNombre, string clienteEmail,
        DateOnly hoy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!await db.Obligados.IgnoreQueryFilters().AnyAsync(x => x.Id == tenantId, cancellationToken))
        {
            db.Obligados.Add(new ObligadoTributario(tenantId, rtn, razonSocial, new Cai(cai), "000-001-01", 1, 1_000_000, hoy.AddYears(1)));
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.Clientes.IgnoreQueryFilters().AnyAsync(x => x.Id == clienteId, cancellationToken))
        {
            db.Clientes.Add(new Cliente(clienteId, tenantId, clienteNombre, null, clienteEmail));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
