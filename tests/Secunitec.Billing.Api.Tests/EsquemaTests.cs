using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Secunitec.Billing.Infrastructure;

namespace Secunitec.Billing.Api.Tests;

// 2.2: la migración escrita a mano (InitialBilling) quedó con Designer y ModelSnapshot; AlinearEsquemaBilling
// renombra sus restricciones e índices a los nombres de EF. Una base ya migrada en la etapa 2.2 se actualiza sin
// perder datos.
public sealed class EsquemaTests : IClassFixture<BillingFactory>
{
    private readonly BillingFactory _factory;

    public EsquemaTests(BillingFactory factory)
    {
        _factory = factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Modelo_SinCambiosPendientesRespectoDelSnapshot()
    {
        using BillingDbContext db = Contexto("Host=localhost;Database=x;Username=x;Password=x");

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task BaseDeLaEtapa22_SeActualizaSinPerderDatos_YConLosNombresDeEf()
    {
        Assert.SkipWhen(_factory.SkipReason is not null, _factory.SkipReason ?? "");
        string database = "upgrade_" + Guid.NewGuid().ToString("N")[..8];
        await using (NpgsqlConnection admin = new(_factory.PostgresConnectionString))
        {
            await admin.OpenAsync(Ct);
            await using NpgsqlCommand create = new($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync(Ct);
        }

        string connection = new NpgsqlConnectionStringBuilder(_factory.PostgresConnectionString) { Database = database }.ConnectionString;
        await using BillingDbContext db = Contexto(connection);
        await AplicarSoloInitialBillingAsync(db);
        Guid obligado = Guid.NewGuid();
        Guid cliente = Guid.NewGuid();
        Guid factura = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing_obligados VALUES ({obligado}, '08011999123456', 'Empresa', 'A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6', '000-001-01', 1, 10, 2, '2026-12-31', true);
            INSERT INTO billing_clientes VALUES ({cliente}, {obligado}, 'Cliente', NULL, NULL);
            INSERT INTO billing_facturas VALUES ({factura}, {obligado}, {cliente}, {Guid.NewGuid()}, 'Emitida', '000-001-01-00000001', 'A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6', '2026-09-22', 100, 15, 115);
            """, Ct);

        await db.Database.MigrateAsync(Ct);

        decimal total = await db.Database.SqlQuery<decimal>($"SELECT total AS \"Value\" FROM billing_facturas WHERE id = {factura}").SingleAsync(Ct);
        Assert.Equal(115m, total);
        HashSet<string> nombres = [.. await db.Database.SqlQuery<string>($"""
            SELECT conname AS "Value" FROM pg_constraint WHERE conrelid::regclass::text LIKE 'billing_%'
            UNION SELECT indexname FROM pg_indexes WHERE tablename LIKE 'billing_%'
            """).ToListAsync(Ct)];
        foreach (IEntityType entity in db.Model.GetEntityTypes())
        {
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (IKey key in entity.GetKeys())
            {
                Assert.Contains(key.GetName(table)!, nombres);
            }

            foreach (IForeignKey foreignKey in entity.GetForeignKeys())
            {
                Assert.Contains(foreignKey.GetConstraintName(table, StoreObjectIdentifier.Table(foreignKey.PrincipalEntityType.GetTableName()!, null))!, nombres);
            }

            foreach (IIndex index in entity.GetIndexes())
            {
                Assert.Contains(index.GetDatabaseName(table)!, nombres);
            }
        }
    }

    // Estado de una base de la etapa 2.2: solo InitialBilling aplicada. Su id tiene 12 dígitos (EF asume 14), así que
    // IMigrator no la encuentra por nombre: se ejecutan sus operaciones y se registra en el historial a mano.
    private static async Task AplicarSoloInitialBillingAsync(BillingDbContext db)
    {
        IMigrationsAssembly migrations = db.GetService<IMigrationsAssembly>();
        const string id = "202609220001_InitialBilling";
        Migration initial = migrations.CreateMigration(migrations.Migrations[id], db.Database.ProviderName!);
        IHistoryRepository history = db.GetService<IHistoryRepository>();
        IEnumerable<MigrationCommand> commands = db.GetService<IMigrationsSqlGenerator>().Generate(initial.UpOperations);
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), Ct);
        foreach (MigrationCommand command in commands)
        {
            await db.Database.ExecuteSqlRawAsync(command.CommandText, Ct);
        }

        await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(id, "10.0.12")), Ct);
    }

    private static BillingDbContext Contexto(string connection) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connection).Options, SinUsuario.Instancia);
}
