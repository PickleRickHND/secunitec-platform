namespace Secunitec.Billing.Domain;

/// <summary>
/// Regla de negocio violada. <see cref="Code"/> es estable (ver <see cref="BillingErrorCodes"/>): la API lo publica
/// en ProblemDetails para que el Front-End y las pruebas no dependan del texto del mensaje.
/// </summary>
public sealed class BillingRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Códigos de error del dominio. Solo se agregan; nunca se renombran.</summary>
public static class BillingErrorCodes
{
    public const string RtnInvalido = "rtn.invalido";
    public const string CaiInvalido = "cai.invalido";
    public const string MontoInvalido = "monto.invalido";
    public const string LineaInvalida = "linea.invalida";
    public const string FacturaInvalida = "factura.invalida";
    public const string FacturaSinLineas = "factura.sin_lineas";
    public const string FacturaDemasiadasLineas = "factura.demasiadas_lineas";
    public const string FacturaEstadoInvalido = "factura.estado_invalido";
    public const string FacturaUltimaLinea = "factura.ultima_linea";
    public const string LineaNoEncontrada = "linea.no_encontrada";
    public const string MotivoRequerido = "anulacion.motivo_requerido";
    public const string ClienteInvalido = "cliente.invalido";
    public const string ObligadoInvalido = "obligado.invalido";
    public const string RangoInvalido = "cai.rango_invalido";
    public const string ObligadoInactivo = "obligado.inactivo";
    public const string CaiVencido = "cai.vencido";
    public const string RangoAgotado = "cai.rango_agotado";
    public const string ObligadoRequerido = "obligado.requerido";
    public const string ClienteAjeno = "cliente.ajeno";
}
