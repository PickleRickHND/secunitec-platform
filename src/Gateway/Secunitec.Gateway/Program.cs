// R01: YARP concentra la entrada pÃºblica y enruta hacia Identity y Billing.
// R02: el Gateway aplica rate limiting antes del proxy para proteger los servicios internos.
// R06: el borde elimina cabeceras de fingerprinting y conserva el hardening comÃºn.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.Security;
using Secunitec.Gateway;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSecunitecKestrel();
builder.Services.AddSecunitecDefaults();

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority =
            builder.Configuration["Jwt:Authority"]
            ?? throw new InvalidOperationException(
                "Configure Jwt:Authority.");
        options.Audience = SecunitecAudiences.Billing;
        options.RequireHttpsMetadata =
            !builder.Environment.IsDevelopment();

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                NameClaimType = SecunitecClaims.Name,
                RoleClaimType = SecunitecClaims.Role
            };
    });

string redis =
    builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Configure Redis:ConnectionString.");

builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redis));

builder.Services.AddSingleton<RedisRateLimiter>();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(
        builder.Configuration.GetSection("ReverseProxy"));

WebApplication app = builder.Build();

app.UseSecunitecDefaults();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
        "/api/billing"))
    {
        AuthenticateResult result =
            await context.AuthenticateAsync(
                JwtBearerDefaults.AuthenticationScheme);

        if (!result.Succeeded)
        {
            await Results.Unauthorized()
                .ExecuteAsync(context);
            return;
        }

        context.User = result.Principal ?? context.User;
    }

    await next(context);
});

app.Use(async (context, next) =>
{
    string policy =
        context.Request.Path.StartsWithSegments(
            "/connect/token")
            ? "token-endpoint"
            : "default";

    string partition =
        policy == "token-endpoint"
            ? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown-ip"
            : context.User.FindFirstValue(
                  SecunitecClaims.Subject)
              ?? context.Connection.RemoteIpAddress?.ToString()
              ?? "unknown";

    int limit =
        policy == "token-endpoint" ? 5 : 60;

    RateLimitDecision decision =
        await context.RequestServices
            .GetRequiredService<RedisRateLimiter>()
            .CheckAsync(
                policy,
                partition,
                limit,
                TimeSpan.FromMinutes(1));

    if (!decision.Allowed)
    {
        context.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;
        context.Response.ContentType =
            "application/json";
        context.Response.Headers.RetryAfter =
            decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await context.Response.WriteAsJsonAsync(
            new
            {
                error = "rate_limited",
                retry_after = decision.RetryAfterSeconds
            });

        return;
    }

    await next(context);
});

app.MapGet("/health",
        () => Results.Ok(
            new
            {
                status = "ok",
                service = "gateway"
            }))
   .AllowAnonymous();

app.MapReverseProxy();

await app.RunAsync();

public partial class Program
{
}