// R03: endpoints de OpenIddict en modo passthrough. OpenIddict valida el protocolo (cliente, PKCE, redirect_uri,
// secreto del cliente confidencial) antes de que la petición llegue aquí; aquí solo se arma el principal.

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Identity;

public static class ConnectEndpoints
{
    /// <summary>Única aplicación autorizada para client credentials (pruebas de carga).</summary>
    public const string JmeterClientId = "jmeter-load";

    public static IEndpointRouteBuilder MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).AllowAnonymous();
        app.MapPost("/connect/token", TokenAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IdentityPrincipalFactory principals)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Solicitud OpenID Connect inválida.");

        AuthenticateResult authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded)
        {
            string returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            return Results.Redirect("/account/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
        }

        ApplicationUser? user = await users.GetUserAsync(authentication.Principal);
        if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        // Consentimiento implícito: spa-secunitec es un cliente propio (decisión documentada en docs/PLAN.md).
        ClaimsPrincipal principal = await principals.CreateUserAsync(user, request.ClientId, request.GetScopes());
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IdentityPrincipalFactory principals,
        IOpenIddictApplicationManager applications,
        IConfiguration configuration)
    {
        OpenIddictRequest request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Solicitud OAuth inválida.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            AuthenticateResult authentication =
                await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            string? subject = authentication.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);

            ApplicationUser? user = subject is null ? null : await users.FindByIdAsync(subject);
            // Un usuario borrado, bloqueado o sin permiso de login ya no puede canjear códigos ni refresh tokens.
            if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            ClaimsPrincipal principal = await principals.CreateUserAsync(user, request.ClientId, request.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            if (!string.Equals(request.ClientId, JmeterClientId, StringComparison.Ordinal))
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            Guid tenantId = Guid.Parse(
                configuration["Identity:JmeterTenantId"]
                    ?? throw new InvalidOperationException("Configure Identity:JmeterTenantId."),
                CultureInfo.InvariantCulture);

            // OpenIddict ya autenticó al cliente con su secreto antes de llegar aquí.
            object application = await applications.FindByClientIdAsync(JmeterClientId)
                ?? throw new InvalidOperationException("La aplicación autenticada no existe.");
            Guid applicationId = Guid.Parse(
                await applications.GetIdAsync(application)
                    ?? throw new InvalidOperationException("La aplicación no tiene identificador."),
                CultureInfo.InvariantCulture);

            ClaimsPrincipal principal = principals.CreateClient(
                applicationId, JmeterClientId, tenantId, SecunitecRoles.Facturador, request.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.BadRequest(new { error = OpenIddictConstants.Errors.UnsupportedGrantType });
    }
}
