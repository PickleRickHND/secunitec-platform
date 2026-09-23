// R03: Identity implementa OAuth 2.0 / OIDC, emisiÃ³n de JWT y endpoints del proveedor.
// R04: el acceso se mantiene deny-by-default y solo los endpoints del protocolo permiten anonimato explÃ­cito.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.Security;
using Secunitec.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSecunitecKestrel();
builder.Services.AddSecunitecDefaults();

string pg = builder.Configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Identity.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(pg);
    options.UseOpenIddict<Guid>();
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "__Host-Secunitec.Identity";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/account/login";
    });

builder.Services.AddAntiforgery();

string mongo = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("Configure Mongo:ConnectionString.");

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase("secunitec_audit"));
builder.Services.AddSingleton<IdentityAuditWriter>();
builder.Services.AddScoped<IdentityPrincipalFactory>();

builder.Services
    .AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>()
               .ReplaceDefaultEntities<Guid>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow();

        // PKCE obligatorio para el cliente SPA.
        options.RequireProofKeyForCodeExchange();

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles,
            OpenIddictConstants.Scopes.OfflineAccess,
            SecunitecAudiences.Billing);

        // JWT firmado, no cifrado.
        options.DisableAccessTokenEncryption();
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));

        options.AddDevelopmentSigningCertificate()
               .AddDevelopmentEncryptionCertificate();

        // Issuer público (la URL del gateway que ve el navegador): el discovery anuncia endpoints alcanzables
        // desde afuera. Billing y el Gateway descargan el JWKS por la red interna (InternalAuthorityHandler).
        options.SetIssuer(new Uri(
            builder.Configuration["Identity:Issuer"]
            ?? throw new InvalidOperationException("Configure Identity:Issuer con la URL pública del gateway.")));

        if (builder.Environment.IsDevelopment())
        {
            options.UseAspNetCore().DisableTransportSecurityRequirement();
        }

        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();

        if (builder.Environment.IsDevelopment())
        {
            options.UseAspNetCore().DisableTransportSecurityRequirement();
        }
    });

WebApplication app = builder.Build();

app.UseSecunitecDefaults();
app.UseAuthentication();
app.UseAuthorization();

await InitializeIdentityAsync(app);

app.MapGet("/health",
        () => Results.Ok(new { status = "ok", service = "identity" }))
   .AllowAnonymous();

app.MapGet("/account/login", (
    HttpContext context,
    IAntiforgery antiforgery) =>
{
    string returnUrl = context.Request.Query["returnUrl"].ToString();
    if (!IsLocalReturnUrl(returnUrl))
    {
        returnUrl = "/";
    }

    var tokens = antiforgery.GetAndStoreTokens(context);

    string html = $"""
<!doctype html>
<html><head><meta charset="utf-8"><title>Secunitec Login</title></head>
<body>
<h1>Secunitec Identity</h1>
<form method="post" action="/account/login">
<input type="hidden" name="__RequestVerificationToken" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
<input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl)}">
<label>Email <input name="email" type="email" autocomplete="username" required></label><br>
<label>Password <input name="password" type="password" autocomplete="current-password" required></label><br>
<button type="submit">Login</button>
</form>
<p><a href="/account/register">Register</a></p>
</body></html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
}).AllowAnonymous();

app.MapPost("/account/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentityAuditWriter audit,
    CancellationToken ct) =>
{
    await antiforgery.ValidateRequestAsync(context);

    string email = context.Request.Form["email"].ToString().Trim();
    string password = context.Request.Form["password"].ToString();

    string returnUrl = context.Request.Form["returnUrl"].ToString();
    if (!IsLocalReturnUrl(returnUrl))
    {
        returnUrl = "/";
    }

    ApplicationUser? user = await users.FindByEmailAsync(email);

    SignInResult result = user is null
        ? SignInResult.Failed
        : await signIn.PasswordSignInAsync(
            user,
            password,
            isPersistent: false,
            lockoutOnFailure: true);

    await audit.WriteAsync(
        result.IsLockedOut ? "login_locked_out" : "login",
        result.Succeeded,
        user?.Id,
        user?.TenantId,
        context.Connection.RemoteIpAddress?.ToString(),
        context.TraceIdentifier,
        result.Succeeded ? "success" : "invalid_credentials_or_locked",
        ct);

    if (result.Succeeded)
    {
        return Results.Redirect(returnUrl);
    }

    return result.IsLockedOut
        ? Results.Problem(
            statusCode: StatusCodes.Status423Locked,
            title: "Cuenta bloqueada")
        : Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Credenciales invÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡lidas");
}).AllowAnonymous();

app.MapGet("/account/register", (
    HttpContext context,
    IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);

    string html = $"""
<!doctype html>
<html><head><meta charset="utf-8"><title>Secunitec Register</title></head>
<body>
<h1>Registro</h1>
<form method="post" action="/account/register">
<input type="hidden" name="__RequestVerificationToken" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
<label>Email <input name="email" type="email" required></label><br>
<label>Password <input name="password" type="password" required></label><br>
<button type="submit">Register</button>
</form>
</body></html>
""";

    return Results.Content(html, "text/html; charset=utf-8");
}).AllowAnonymous();

app.MapPost("/account/register", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserManager<ApplicationUser> users,
    IConfiguration configuration,
    IdentityAuditWriter audit,
    CancellationToken ct) =>
{
    await antiforgery.ValidateRequestAsync(context);

    string email = context.Request.Form["email"].ToString().Trim();
    string password = context.Request.Form["password"].ToString();

    if (!Guid.TryParse(
        configuration["Identity:RegistrationTenantId"],
        out Guid tenantId))
    {
        throw new InvalidOperationException(
            "Identity:RegistrationTenantId debe ser un GUID.");
    }

    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        TenantId = tenantId
    };

    IdentityResult result = await users.CreateAsync(user, password);

    if (!result.Succeeded)
    {
        await audit.WriteAsync(
            "register",
            false,
            user.Id,
            tenantId,
            context.Connection.RemoteIpAddress?.ToString(),
            context.TraceIdentifier,
            string.Join("; ", result.Errors.Select(x => x.Code)),
            ct);

        return Results.ValidationProblem(
            result.Errors
                .GroupBy(x => x.Code)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Description).ToArray()));
    }

    await users.AddToRoleAsync(user, SecunitecRoles.Cliente);

    await audit.WriteAsync(
        "register",
        true,
        user.Id,
        tenantId,
        context.Connection.RemoteIpAddress?.ToString(),
        context.TraceIdentifier,
        "created",
        ct);

    return Results.Redirect("/account/login");
}).AllowAnonymous();

app.MapGet("/connect/authorize", async (
    HttpContext context,
    UserManager<ApplicationUser> users,
    IdentityPrincipalFactory principals) =>
{
    var request = context.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException(
            "Solicitud OpenID Connect invÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡lida.");

    var authentication = await context.AuthenticateAsync(
        IdentityConstants.ApplicationScheme);

    if (!authentication.Succeeded)
    {
        string returnUrl =
            context.Request.PathBase +
            context.Request.Path +
            context.Request.QueryString;

        return Results.Redirect(
            "/account/login?returnUrl=" +
            Uri.EscapeDataString(returnUrl));
    }

    string? subject =
        authentication.Principal?.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

    if (!Guid.TryParse(subject, out Guid userId))
    {
        return Results.Unauthorized();
    }

    ApplicationUser? user =
        await users.FindByIdAsync(userId.ToString());

    if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
    {
        return Results.Unauthorized();
    }

    ClaimsPrincipal principal =
        await principals.CreateUserAsync(
            user,
            request.ClientId,
            request.GetScopes());

    return Results.SignIn(
        principal,
        authenticationScheme:
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}).AllowAnonymous();

app.MapPost("/connect/token", async (
    HttpContext context,
    UserManager<ApplicationUser> users,
    IdentityPrincipalFactory principals,
    IConfiguration configuration) =>
{
    var request = context.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException(
            "Solicitud OAuth invÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¡lida.");

    if (request.IsAuthorizationCodeGrantType() ||
        request.IsRefreshTokenGrantType())
    {
        var authentication = await context.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!authentication.Succeeded ||
            authentication.Principal is null)
        {
            return Results.Unauthorized();
        }

        string? subject =
            authentication.Principal
                .FindFirst(OpenIddictConstants.Claims.Subject)?.Value;

        if (!Guid.TryParse(subject, out Guid userId))
        {
            return Results.Unauthorized();
        }

        ApplicationUser? user =
            await users.FindByIdAsync(userId.ToString());

        if (user is null || user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Results.Unauthorized();
        }

        ClaimsPrincipal principal =
            await principals.CreateUserAsync(
                user,
                request.ClientId,
                request.GetScopes());

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    if (request.IsClientCredentialsGrantType())
    {
        if (!string.Equals(
            request.ClientId,
            "jmeter-load",
            StringComparison.Ordinal))
        {
            return Results.BadRequest(
                new { error = "unauthorized_client" });
        }

        if (!Guid.TryParse(
            configuration["Identity:JmeterTenantId"],
            out Guid tenantId))
        {
            throw new InvalidOperationException(
                "Identity:JmeterTenantId debe ser un GUID.");
        }

        ClaimsPrincipal principal =
            principals.CreateClient(
                "jmeter-load",
                tenantId,
                SecunitecRoles.Facturador,
                request.GetScopes());

        return Results.SignIn(
            principal,
            authenticationScheme:
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    return Results.BadRequest(
        new { error = "unsupported_grant_type" });
}).AllowAnonymous();

app.MapGet("/",
    () => Results.Content(
        "<html><body><h1>Secunitec Identity</h1></body></html>",
        "text/html; charset=utf-8"))
   .AllowAnonymous();

await app.RunAsync();

static bool IsLocalReturnUrl(string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    value.StartsWith('/') &&
    !value.StartsWith("//", StringComparison.Ordinal) &&
    !value.StartsWith('\\');

static async Task InitializeIdentityAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();
    var roles = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var users = scope.ServiceProvider
        .GetRequiredService<UserManager<ApplicationUser>>();
    var applications = scope.ServiceProvider
        .GetRequiredService<IOpenIddictApplicationManager>();

    await db.Database.MigrateAsync();

    foreach (string role in new[]
    {
        SecunitecRoles.Admin,
        SecunitecRoles.Facturador,
        SecunitecRoles.Auditor,
        SecunitecRoles.Cliente
    })
    {
        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(
                new IdentityRole<Guid>(role));
        }
    }

    if (!Guid.TryParse(
        app.Configuration["Identity:SeedAdminTenantId"],
        out Guid adminTenantId))
    {
        throw new InvalidOperationException(
            "Identity:SeedAdminTenantId debe ser un GUID.");
    }

    string adminEmail =
        app.Configuration["Identity:SeedAdminEmail"]
        ?? throw new InvalidOperationException(
            "Configure Identity:SeedAdminEmail.");

    string adminPassword =
        app.Configuration["Identity:SeedAdminPassword"]
        ?? throw new InvalidOperationException(
            "Configure Identity:SeedAdminPassword.");

    if (await users.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            TenantId = adminTenantId
        };

        IdentityResult result =
            await users.CreateAsync(admin, adminPassword);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(
                    "; ",
                    result.Errors.Select(x => x.Description)));
        }

        await users.AddToRoleAsync(
            admin,
            SecunitecRoles.Admin);
    }

    string clientSecret =
        app.Configuration["Identity:JmeterClientSecret"]
        ?? throw new InvalidOperationException(
            "Configure Identity:JmeterClientSecret.");

    if (await applications.FindByClientIdAsync("spa-secunitec")
        is null)
    {
        await applications.CreateAsync(
            new OpenIddictApplicationDescriptor
            {
                ClientId = "spa-secunitec",
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                DisplayName = "Secunitec SPA",
                RedirectUris =
                {
                    new Uri(
                        "http://localhost:3000/auth/callback")
                },
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
                    OpenIddictConstants.Permissions.Prefixes.Scope +
                        OpenIddictConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Permissions.Prefixes.Scope +
                        SecunitecAudiences.Billing
                },
                Requirements =
                {
                    OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
                }
            });
    }

    if (await applications.FindByClientIdAsync("jmeter-load")
        is null)
    {
        await applications.CreateAsync(
            new OpenIddictApplicationDescriptor
            {
                ClientId = "jmeter-load",
                ClientSecret = clientSecret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                DisplayName = "JMeter Load Test",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope +
                        SecunitecAudiences.Billing
                }
            });
    }
}

public partial class Program
{
}
