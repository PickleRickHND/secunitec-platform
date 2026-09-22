using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Secunitec.BuildingBlocks.AspNetCore.Security;
using Secunitec.BuildingBlocks.Security;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Security;

/// <summary>Matriz RBAC de docs/PLAN.md §3.2 evaluada con el IAuthorizationService real.</summary>
public sealed class SecunitecPoliciesTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecunitecAuthorization();
        return services.BuildServiceProvider();
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal principal, string policy)
    {
        await using ServiceProvider provider = BuildProvider();
        IAuthorizationService authorization = provider.GetRequiredService<IAuthorizationService>();
        AuthorizationResult result = await authorization.AuthorizeAsync(principal, resource: null, policy);
        return result.Succeeded;
    }

    public static TheoryData<string, string, bool> Matriz()
    {
        TheoryData<string, string, bool> data = [];

        // Admin: todo.
        foreach (string policy in SecunitecPolicies.All)
        {
            data.Add(SecunitecRoles.Admin, policy, true);
        }

        // Facturador: facturas y clientes; ni auditoría ni obligados ni admin.
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.ManageInvoices, true);
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.ReadInvoices, true);
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.ManageClientes, true);
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.ReadAudit, false);
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.ManageObligados, false);
        data.Add(SecunitecRoles.Facturador, SecunitecPolicies.Admin, false);

        // Auditor: solo lectura.
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.ReadInvoices, true);
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.ReadAudit, true);
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.ManageInvoices, false);
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.ManageClientes, false);
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.ManageObligados, false);
        data.Add(SecunitecRoles.Auditor, SecunitecPolicies.Admin, false);

        // Cliente: solo leer facturas (el filtro por cliente_id es ABAC en Application).
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.ReadInvoices, true);
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.ReadAudit, false);
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.ManageInvoices, false);
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.ManageClientes, false);
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.ManageObligados, false);
        data.Add(SecunitecRoles.Cliente, SecunitecPolicies.Admin, false);

        return data;
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    public async Task Politica_EvaluaLaMatrizDeRoles(string role, string policy, bool esperado)
    {
        bool resultado = await AuthorizeAsync(Principals.WithRoles(role), policy);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public async Task Politica_ConRolDesconocido_Deniega()
    {
        Assert.False(await AuthorizeAsync(Principals.WithRoles("SuperAdmin"), SecunitecPolicies.ReadInvoices));
    }

    [Fact]
    public async Task Politica_ConVariosRoles_BastaConUnoPermitido()
    {
        Assert.True(await AuthorizeAsync(Principals.WithRoles(SecunitecRoles.Cliente, SecunitecRoles.Auditor), SecunitecPolicies.ReadAudit));
    }

    [Fact]
    public async Task Politica_SinTenant_DeniegaAunqueElRolAlcance()
    {
        // tenant_id es obligatorio en todo token de usuario (contrato del token).
        Assert.False(await AuthorizeAsync(Principals.WithoutTenant(SecunitecRoles.Admin), SecunitecPolicies.Admin));
    }

    [Fact]
    public async Task FallbackPolicy_R04_DeniegaAnonimoYExigeTenant()
    {
        await using ServiceProvider provider = BuildProvider();
        AuthorizationOptions options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;
        IAuthorizationService authorization = provider.GetRequiredService<IAuthorizationService>();

        Assert.NotNull(options.FallbackPolicy);
        Assert.False((await authorization.AuthorizeAsync(Principals.Anonymous(), resource: null, options.FallbackPolicy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Principals.WithoutTenant(SecunitecRoles.Admin), resource: null, options.FallbackPolicy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Principals.WithRoles(SecunitecRoles.Cliente), resource: null, options.FallbackPolicy)).Succeeded);
    }

    [Fact]
    public void RolesByPolicy_CadaPoliticaTieneAlMenosUnRolConocido()
    {
        foreach ((string policy, IReadOnlyList<string> roles) in SecunitecPolicies.RolesByPolicy)
        {
            Assert.NotEmpty(roles);
            Assert.All(roles, r => Assert.True(SecunitecRoles.IsKnown(r), $"{policy}: rol desconocido {r}"));
            Assert.Contains(SecunitecRoles.Admin, roles);
        }
    }
}
