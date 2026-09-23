using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Identity;

/// <summary>Migraciones y datos iniciales: roles, administrador y clientes OAuth (idempotente).</summary>
public static class IdentitySeeder
{
    public const string SpaClientId = "spa-secunitec";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;

        await provider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        RoleManager<IdentityRole<Guid>> roles = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (string role in new[] { SecunitecRoles.Admin, SecunitecRoles.Facturador, SecunitecRoles.Auditor, SecunitecRoles.Cliente })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        UserManager<ApplicationUser> users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        Guid tenantId = RequiredGuid(configuration, "Identity:SeedAdminTenantId");
        await EnsureUserAsync(users,
            configuration["Identity:SeedAdminEmail"] ?? throw new InvalidOperationException("Configure Identity:SeedAdminEmail."),
            configuration["Identity:SeedAdminPassword"] ?? throw new InvalidOperationException("Configure Identity:SeedAdminPassword."),
            tenantId, SecunitecRoles.Admin, clienteId: null);

        await EnsureClientsAsync(provider.GetRequiredService<IOpenIddictApplicationManager>(), configuration);
    }

    private static async Task EnsureClientsAsync(IOpenIddictApplicationManager applications, IConfiguration configuration)
    {
        if (await applications.FindByClientIdAsync(SpaClientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = SpaClientId,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                DisplayName = "Secunitec SPA",
                RedirectUris = { new Uri(configuration["Identity:SpaRedirectUri"] ?? "http://localhost:3000/auth/callback") },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Permissions.Prefixes.Scope + SecunitecAudiences.Billing,
                },
                Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange },
            });
        }

        if (await applications.FindByClientIdAsync(ConnectEndpoints.JmeterClientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = ConnectEndpoints.JmeterClientId,
                ClientSecret = configuration["Identity:JmeterClientSecret"]
                    ?? throw new InvalidOperationException("Configure Identity:JmeterClientSecret."),
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                DisplayName = "JMeter Load Test",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + SecunitecAudiences.Billing,
                },
            });
        }
    }

    internal static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users, string email, string password, Guid tenantId, string role, Guid? clienteId)
    {
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        ApplicationUser user = new()
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = tenantId,
            ClienteId = clienteId,
        };

        IdentityResult created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo crear {email}: " + string.Join("; ", created.Errors.Select(error => error.Description)));
        }

        await users.AddToRoleAsync(user, role);
    }

    internal static Guid RequiredGuid(IConfiguration configuration, string key) =>
        Guid.Parse(configuration[key] ?? throw new InvalidOperationException($"Configure {key}."), CultureInfo.InvariantCulture);
}
