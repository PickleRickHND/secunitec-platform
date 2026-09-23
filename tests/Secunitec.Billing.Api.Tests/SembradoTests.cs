using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secunitec.Billing.Infrastructure;

namespace Secunitec.Billing.Api.Tests;

// 5.3: el tenant de carga de los clientes jmeter-load-NN convive con el de ejemplo (RTN único) y el sembrado es
// idempotente: Billing lo corre en cada arranque en Development.
public sealed class SembradoTests : IClassFixture<BillingFactory>
{
    private readonly BillingFactory _factory;

    public SembradoTests(BillingFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenantDeCarga_ConviveConElDeEjemplo_YEsIdempotente()
    {
        Assert.SkipWhen(_factory.SkipReason is not null, _factory.SkipReason ?? "");
        string database = "sembrado_" + Guid.NewGuid().ToString("N")[..8];
        await using (NpgsqlConnection admin = new(_factory.PostgresConnectionString))
        {
            await admin.OpenAsync(Ct);
            await using NpgsqlCommand create = new($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(Ct);
        }

        string connection = new NpgsqlConnectionStringBuilder(_factory.PostgresConnectionString) { Database = database }.ConnectionString;
        await using BillingDbContext db = new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connection).Options, SinUsuario.Instancia);
        await db.Database.MigrateAsync(Ct);
        Guid demo = Guid.NewGuid();
        Guid carga = Guid.NewGuid();
        Guid clienteDemo = Guid.NewGuid();
        Guid clienteCarga = Guid.NewGuid();
        DateOnly hoy = new(2026, 9, 23);

        for (int arranque = 0; arranque < 2; arranque++)
        {
            await BillingDemoSeeder.SeedAsync(db, demo, clienteDemo, hoy, Ct);
            await BillingDemoSeeder.SeedLoadTenantAsync(db, carga, clienteCarga, hoy, Ct);
        }

        var obligados = await db.Obligados.IgnoreQueryFilters().Select(x => new { x.Id, Rtn = x.Rtn.Value }).ToListAsync(Ct);
        Assert.Equal(2, obligados.Count);
        Assert.Equal(2, obligados.Select(x => x.Rtn).Distinct().Count());
        Assert.Contains(obligados, x => x.Id == carga);
        Assert.Equal([clienteCarga], await db.Clientes.IgnoreQueryFilters().Where(x => x.ObligadoId == carga).Select(x => x.Id).ToListAsync(Ct));
    }
}
