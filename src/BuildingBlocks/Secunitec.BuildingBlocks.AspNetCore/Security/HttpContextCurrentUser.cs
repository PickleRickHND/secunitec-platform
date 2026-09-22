using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.BuildingBlocks.AspNetCore.Security;

/// <summary>Implementación de <see cref="ICurrentUser"/> sobre el principal de la petición HTTP actual.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Principal?.GetUserId();

    public Guid? TenantId => Principal?.GetTenantId();

    public Guid? ClienteId => Principal?.GetClienteId();

    public string? ClientId => Principal?.GetClientId();

    public IReadOnlyCollection<string> Roles => Principal?.GetRoles() ?? [];

    public bool IsInRole(string role) => Principal?.HasSecunitecRole(role) == true;
}

/// <summary>Registro de <see cref="ICurrentUser"/>.</summary>
public static class CurrentUserExtensions
{
    /// <summary>Registra <see cref="IHttpContextAccessor"/> y <see cref="ICurrentUser"/> (scoped).</summary>
    public static IServiceCollection AddSecunitecCurrentUser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        return services;
    }
}
