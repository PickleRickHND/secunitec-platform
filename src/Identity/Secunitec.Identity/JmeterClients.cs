using System.Globalization;

namespace Secunitec.Identity;

/// <summary>
/// Clientes confidenciales de client credentials (pruebas de carga, PLAN §0):
/// <list type="bullet">
/// <item><c>jmeter-load</c>, en el tenant de ejemplo (<c>Identity:JmeterTenantId</c>).</item>
/// <item>Solo en Development, <c>jmeter-load-01</c> a <c>jmeter-load-NN</c> (<c>Identity:JmeterLoadClients</c>), en su
/// propio tenant de carga (<c>Identity:JmeterLoadTenantId</c>). Cada uno tiene su <c>sub</c> y, por eso, su partición de
/// 60 peticiones por minuto en el gateway: JMeter reparte los hilos entre ellos sin bajar los límites (etapa 5.3).</item>
/// </list>
/// Se autoriza solo el conjunto exacto configurado, nunca un prefijo.
/// </summary>
public static class JmeterClients
{
    public const string LoadClientsKey = "Identity:JmeterLoadClients";
    public const string LoadTenantKey = "Identity:JmeterLoadTenantId";

    /// <summary>Tope de clientes de carga: cada uno cuesta un canje de /connect/token (5 por minuto por IP).</summary>
    public const int MaxLoadClients = 50;

    /// <summary>Ids de los clientes de carga configurados; vacío fuera de Development o sin configurar.</summary>
    public static IReadOnlyList<string> LoadClientIds(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        int count = LoadClientCount(configuration);
        return count == 0 || !environment.IsDevelopment()
            ? []
            : Enumerable.Range(1, count).Select(n => $"{ConnectEndpoints.JmeterClientId}-{n.ToString("00", CultureInfo.InvariantCulture)}").ToArray();
    }

    public static bool IsAuthorized(string? clientId, IConfiguration configuration, IHostEnvironment environment) =>
        string.Equals(clientId, ConnectEndpoints.JmeterClientId, StringComparison.Ordinal) ||
        LoadClientIds(configuration, environment).Contains(clientId, StringComparer.Ordinal);

    /// <summary>Tenant del token: el de ejemplo para <c>jmeter-load</c>; el de carga para los demás.</summary>
    public static Guid TenantOf(string clientId, IConfiguration configuration) =>
        IdentitySeeder.RequiredGuid(configuration,
            string.Equals(clientId, ConnectEndpoints.JmeterClientId, StringComparison.Ordinal) ? "Identity:JmeterTenantId" : LoadTenantKey);

    internal static int LoadClientCount(IConfiguration configuration)
    {
        string? value = configuration[LoadClientsKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        // Un valor mal escrito falla al arrancar en vez de dejar la prueba de carga sin clientes sin avisar.
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count is >= 0 and <= MaxLoadClients
            ? count
            : throw new InvalidOperationException($"{LoadClientsKey} admite de 0 a {MaxLoadClients} clientes.");
    }
}
