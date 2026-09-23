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
            RequiredSecret(configuration, "Identity:SeedAdminPassword"),
            tenantId, SecunitecRoles.Admin, clienteId: null);

        // Usuarios de demostración, uno por rol, en el tenant del administrador (H2). Solo si hay contraseña de
        // demo configurada; el cliente_id apunta al cliente que Billing siembra en Development.
        if (!string.IsNullOrWhiteSpace(configuration["Identity:SeedDemoPassword"]))
        {
            string demoPassword = RequiredSecret(configuration, "Identity:SeedDemoPassword");
            await EnsureUserAsync(users, "facturador@secunitec.local", demoPassword, tenantId, SecunitecRoles.Facturador, clienteId: null);
            await EnsureUserAsync(users, "auditor@secunitec.local", demoPassword, tenantId, SecunitecRoles.Auditor, clienteId: null);
            await EnsureUserAsync(users, "cliente@secunitec.local", demoPassword, tenantId, SecunitecRoles.Cliente,
                RequiredGuid(configuration, "Identity:SeedClienteId"));
        }

        await EnsureClientsAsync(
            provider.GetRequiredService<IOpenIddictApplicationManager>(), configuration, provider.GetRequiredService<IHostEnvironment>());
    }

    private static async Task EnsureClientsAsync(
        IOpenIddictApplicationManager applications, IConfiguration configuration, IHostEnvironment environment)
    {
        // El cliente del SPA se sincroniza en cada arranque: una base creada antes de la etapa 4.1 no tenía el permiso
        // de cierre de sesión ni la URI de vuelta, y cambiar SECUNITEC_SPA_URL no debe exigir borrar la base.
        OpenIddictApplicationDescriptor spa = SpaDescriptor(configuration);
        object? existing = await applications.FindByClientIdAsync(SpaClientId);
        if (existing is null)
        {
            await applications.CreateAsync(spa);
        }
        else
        {
            await applications.UpdateAsync(existing, spa);
        }

        // Etapa 5.3: los clientes de carga solo existen en Development; en otro ambiente la configuración se rechaza.
        if (JmeterClients.LoadClientCount(configuration) > 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException($"{JmeterClients.LoadClientsKey} solo se admite en Development.");
        }

        string jmeterSecret = RequiredSecret(configuration, "Identity:JmeterClientSecret");
        foreach (string clientId in JmeterClients.LoadClientIds(configuration, environment).Prepend(ConnectEndpoints.JmeterClientId))
        {
            if (await applications.FindByClientIdAsync(clientId) is null)
            {
                await applications.CreateAsync(JmeterDescriptor(clientId, jmeterSecret));
            }
        }
    }

    private static OpenIddictApplicationDescriptor JmeterDescriptor(string clientId, string secret) => new()
    {
        ClientId = clientId,
        ClientSecret = secret,
        ClientType = OpenIddictConstants.ClientTypes.Confidential,
        ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
        DisplayName = "JMeter Load Test",
        Permissions =
        {
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            OpenIddictConstants.Permissions.Prefixes.Scope + SecunitecAudiences.Billing,
        },
    };

    /// <summary>URL del SPA sin barra final (<c>Identity:SpaUrl</c>); de ella salen las URIs de redirección.</summary>
    internal static string SpaUrl(IConfiguration configuration) =>
        (configuration["Identity:SpaUrl"] is { Length: > 0 } url ? url : "http://localhost:3000").TrimEnd('/');

    internal static OpenIddictApplicationDescriptor SpaDescriptor(IConfiguration configuration)
    {
        string spaUrl = SpaUrl(configuration);
        return new OpenIddictApplicationDescriptor
        {
            ClientId = SpaClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "Secunitec SPA",
            RedirectUris = { new Uri(spaUrl + "/auth/callback") },
            PostLogoutRedirectUris = { new Uri(spaUrl + "/auth/logout-callback") },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
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
        };
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

    // Los valores CAMBIAR_* de .env.example son públicos (están en el repo): con ellos cualquiera obtendría
    // tokens de jmeter-load o entraría como administrador.
    internal static string RequiredSecret(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configure {key}.");
        }

        return value.StartsWith("CAMBIAR", StringComparison.Ordinal)
            ? throw new InvalidOperationException($"{key} conserva el valor de ejemplo de .env.example; defina uno propio.")
            : value;
    }

    internal static Guid RequiredGuid(IConfiguration configuration, string key) =>
        Guid.Parse(configuration[key] ?? throw new InvalidOperationException($"Configure {key}."), CultureInfo.InvariantCulture);
}
