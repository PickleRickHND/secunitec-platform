// Soporte de diseÃ±o: esta fÃ¡brica se utiliza por EF Core para crear migraciones fuera del host.
// No participa en el flujo HTTP ni en la autorizaciÃ³n del servicio.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Secunitec.Identity;

public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<ApplicationDbContext>();

        string connectionString =
            Environment.GetEnvironmentVariable(
                "SECUNITEC_IDENTITY_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=secunitec_identity;Username=identity_app;Password=design-time-only;Include Error Detail=false";

        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.UseOpenIddict<Guid>();

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
