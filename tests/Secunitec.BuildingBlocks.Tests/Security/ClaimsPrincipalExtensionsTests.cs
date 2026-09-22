using System.Security.Claims;
using Secunitec.BuildingBlocks.Security;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Security;

public sealed class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetTenantId_ConClaimValido_DevuelveGuid()
    {
        ClaimsPrincipal principal = Principals.WithRoles(SecunitecRoles.Facturador);

        Assert.Equal(Principals.Tenant, principal.GetTenantId());
        Assert.Equal(Principals.User, principal.GetUserId());
    }

    [Fact]
    public void GetTenantId_SinClaim_DevuelveNull()
    {
        ClaimsPrincipal principal = Principals.WithoutTenant(SecunitecRoles.Facturador);

        Assert.Null(principal.GetTenantId());
    }

    [Theory]
    [InlineData("no-es-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void GetTenantId_ConValorInvalido_DevuelveNullSinLanzar(string raw)
    {
        ClaimsPrincipal principal = Principals.Build(
            tenant: null,
            roles: [SecunitecRoles.Admin],
            extra: [new Claim(SecunitecClaims.TenantId, raw)]);

        Assert.Null(principal.GetTenantId());
    }

    [Fact]
    public void GetRoles_ConVariosClaimsRole_DevuelveTodosSinDuplicados()
    {
        ClaimsPrincipal principal = Principals.WithRoles(SecunitecRoles.Admin, SecunitecRoles.Auditor, SecunitecRoles.Admin);

        IReadOnlyCollection<string> roles = principal.GetRoles();

        Assert.Equal(2, roles.Count);
        Assert.Contains(SecunitecRoles.Admin, roles);
        Assert.Contains(SecunitecRoles.Auditor, roles);
        Assert.True(principal.HasSecunitecRole(SecunitecRoles.Auditor));
        Assert.False(principal.HasSecunitecRole(SecunitecRoles.Facturador));
    }

    [Fact]
    public void HasSecunitecRole_EsSensibleAMayusculas()
    {
        ClaimsPrincipal principal = Principals.WithRoles(SecunitecRoles.Admin);

        Assert.False(principal.HasSecunitecRole("admin"));
    }

    [Fact]
    public void GetClienteId_ConRolCliente_DevuelveGuid()
    {
        ClaimsPrincipal principal = Principals.ClienteUser();

        Assert.Equal(Principals.Cliente, principal.GetClienteId());
    }

    [Fact]
    public void GetClienteId_SinRolCliente_IgnoraElClaim()
    {
        // Un Facturador con cliente_id en el token no debe verse como Cliente: el claim solo tiene sentido con ese rol.
        ClaimsPrincipal principal = Principals.Build(
            Principals.Tenant,
            [SecunitecRoles.Facturador],
            extra: [new Claim(SecunitecClaims.ClienteId, Principals.Cliente.ToString())]);

        Assert.Null(principal.GetClienteId());
    }

    [Fact]
    public void GetClientId_ConTokenDeAplicacion_DevuelveValor()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(SecunitecClaims.ClientId, "jmeter-load")],
            authenticationType: "Test"));

        Assert.Equal("jmeter-load", principal.GetClientId());
        Assert.Null(principal.GetUserId());
    }

    [Fact]
    public void Anonimo_DevuelveNullEnTodo()
    {
        ClaimsPrincipal principal = Principals.Anonymous();

        Assert.Null(principal.GetUserId());
        Assert.Null(principal.GetTenantId());
        Assert.Null(principal.GetClienteId());
        Assert.Null(principal.GetClientId());
        Assert.Empty(principal.GetRoles());
    }

    [Fact]
    public void SecunitecRoles_All_ContieneLosCuatroRolesDelContrato()
    {
        Assert.Equal([SecunitecRoles.Admin, SecunitecRoles.Facturador, SecunitecRoles.Auditor, SecunitecRoles.Cliente], SecunitecRoles.All);
        Assert.True(SecunitecRoles.IsKnown(SecunitecRoles.Cliente));
        Assert.False(SecunitecRoles.IsKnown("SuperAdmin"));
    }
}
