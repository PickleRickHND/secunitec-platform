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
    /// <summary>
    /// Aplicación de client credentials del tenant de ejemplo. Los clientes de carga de la etapa 5.3
    /// (<c>jmeter-load-NN</c>, solo en Development) están en <see cref="JmeterClients"/>.
    /// </summary>
    public const string JmeterClientId = "jmeter-load";

    public static IEndpointRouteBuilder MapConnectEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).AllowAnonymous();
        app.MapPost("/connect/token", TokenAsync).AllowAnonymous();
        app.MapMethods("/connect/endsession", [HttpMethods.Get, HttpMethods.Post], EndSessionAsync).AllowAnonymous();
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
        if (!authentication.Succeeded && request.HasPromptValue(OpenIddictConstants.PromptValues.None))
        {
            // OIDC Core §3.1.2.6: con prompt=none no se muestra el login; se responde login_required al cliente.
            return Results.Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.LoginRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "No hay una sesión iniciada.",
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

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
        IConfiguration configuration,
        IHostEnvironment environment)
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

            // Los scopes salen del código o del refresh token, no del request: en el canje del código el request no
            // trae scope, y sin él el access token quedaba sin audiencia secunitec-billing (Billing respondía 401).
            ClaimsPrincipal principal = await principals.CreateUserAsync(
                user, request.ClientId, authentication.Principal!.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // Solo el conjunto exacto de clientes configurados (JmeterClients), nunca un prefijo.
            if (!JmeterClients.IsAuthorized(request.ClientId, configuration, environment))
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            string clientId = request.ClientId!;
            Guid tenantId = JmeterClients.TenantOf(clientId, configuration);

            // OpenIddict ya autenticó al cliente con su secreto antes de llegar aquí. El sub es el id de su aplicación:
            // cada cliente de carga tiene su propia partición en el rate limiting del gateway.
            object application = await applications.FindByClientIdAsync(clientId)
                ?? throw new InvalidOperationException("La aplicación autenticada no existe.");
            Guid applicationId = Guid.Parse(
                await applications.GetIdAsync(application)
                    ?? throw new InvalidOperationException("La aplicación no tiene identificador."),
                CultureInfo.InvariantCulture);

            ClaimsPrincipal principal = principals.CreateClient(
                applicationId, clientId, tenantId, SecunitecRoles.Facturador, request.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.BadRequest(new { error = OpenIddictConstants.Errors.UnsupportedGrantType });
    }

    // Cierre de sesión OIDC (RP-Initiated Logout). OpenIddict ya validó post_logout_redirect_uri contra el cliente;
    // aquí se borra la cookie de Identity y OpenIddict redirige de vuelta al SPA.
    private static async Task<IResult> EndSessionAsync(
        HttpContext context, UserManager<ApplicationUser> users, IdentityAuditWriter audit)
    {
        AuthenticateResult authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        ApplicationUser? user = authentication.Succeeded ? await users.GetUserAsync(authentication.Principal) : null;
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);

        if (user is not null)
        {
            await audit.WriteAsync(context, "logout", success: true, user.Id, user.TenantId, detail: null, context.RequestAborted);
        }

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
