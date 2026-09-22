using System.Security.Claims;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.BuildingBlocks.Tests.Support;

/// <summary>Constructores de principals con la forma exacta del contrato del token.</summary>
internal static class Principals
{
    public static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Cliente = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    public static ClaimsPrincipal WithRoles(params string[] roles) => Build(Tenant, roles);

    public static ClaimsPrincipal WithoutTenant(params string[] roles) => Build(tenant: null, roles);

    public static ClaimsPrincipal ClienteUser() =>
        Build(Tenant, [SecunitecRoles.Cliente], extra: [new Claim(SecunitecClaims.ClienteId, Cliente.ToString())]);

    public static ClaimsPrincipal Build(Guid? tenant, IEnumerable<string> roles, IEnumerable<Claim>? extra = null)
    {
        List<Claim> claims = [new Claim(SecunitecClaims.Subject, User.ToString())];
        if (tenant is { } t)
        {
            claims.Add(new Claim(SecunitecClaims.TenantId, t.ToString()));
        }

        claims.AddRange(roles.Select(r => new Claim(SecunitecClaims.Role, r)));
        if (extra is not null)
        {
            claims.AddRange(extra);
        }

        // authenticationType no nulo => IsAuthenticated = true, igual que un JWT validado.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }
}
