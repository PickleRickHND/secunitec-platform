// Soporte de diseño: EF Core usa esta fábrica para crear migraciones fuera del host (dotnet ef).
// No participa en el flujo HTTP ni en la autorización del servicio.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Infrastructure;

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("SECUNITEC_BILLING_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=secunitec_billing;Username=billing_app;Password=design-time-only;Include Error Detail=false";

        return new BillingDbContext(
            new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connectionString).Options, SinUsuario.Instancia);
    }
}

/// <summary>Usuario vacío para herramientas y tareas sin petición (migraciones, seeder): no ve ningún tenant.</summary>
public sealed class SinUsuario : ICurrentUser
{
    public static SinUsuario Instancia { get; } = new();

    public bool IsAuthenticated => false;
    public Guid? UserId => null;
    public Guid? TenantId => null;
    public Guid? ClienteId => null;
    public string? ClientId => null;
    public IReadOnlyCollection<string> Roles => [];
    public bool IsInRole(string role) => false;
}
