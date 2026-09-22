namespace Secunitec.BuildingBlocks.Security;

/// <summary>Audiencias (<c>aud</c>) que emite Identity y que valida cada servicio.</summary>
public static class SecunitecAudiences
{
    /// <summary>Core de facturación (<c>src/Billing</c>).</summary>
    public const string Billing = "secunitec-billing";

    /// <summary>Endpoints de gestión de usuarios del propio Identity.</summary>
    public const string Identity = "secunitec-identity";
}
