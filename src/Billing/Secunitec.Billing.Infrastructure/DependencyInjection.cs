using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra Postgres, Mongo, Redis y los adaptadores de los puertos. La configuración se lee al resolver los
    /// servicios (no al registrarlos) para que los tests la puedan reemplazar.
    /// </summary>
    public static IServiceCollection AddBillingInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<BillingDbContext>((provider, options) => options
            .UseNpgsql(Required(provider, "ConnectionStrings:Billing"))
            // Las líneas solo se leen a través de su factura, que sí lleva el filtro de tenant.
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        services.AddSingleton<IMongoClient>(provider => new MongoClient(Required(provider, "Mongo:ConnectionString")));
        services.AddSingleton(provider => provider.GetRequiredService<IMongoClient>().GetDatabase("secunitec_audit"));
        // abortConnect=false en la cadena: si Redis no está al arrancar, Billing arranca igual y sigue sin caché.
        services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(Required(provider, "Redis:ConnectionString")));

        services.AddScoped<EfObligadoRepository>();
        services.AddScoped<IObligadoRepository>(provider => provider.GetRequiredService<EfObligadoRepository>());
        services.AddScoped<INumeradorFacturas>(provider => provider.GetRequiredService<EfObligadoRepository>());
        services.AddScoped<IFacturaRepository, EfFacturaRepository>();
        services.AddScoped<IClienteRepository, EfClienteRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAuditoria, MongoAuditoria>();
        services.AddScoped<ICache, RedisCache>();
        services.AddSingleton<IMetricasFacturacion, MetricasFacturacion>();
        return services;
    }

    private static string Required(IServiceProvider provider, string key) =>
        provider.GetRequiredService<IConfiguration>()[key] ?? throw new InvalidOperationException($"Configure {key}.");
}
