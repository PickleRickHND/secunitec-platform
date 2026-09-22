using System.Security.Cryptography;

namespace Secunitec.BuildingBlocks.Http;

/// <summary>
/// Identificador de correlación que viaja en la cabecera <c>X-Correlation-Id</c> entre gateway,
/// microservicios, auditoría y trazas (docs/PLAN.md §2.2). Formato: 1..64 caracteres <c>[A-Za-z0-9._-]</c>.
/// </summary>
public static class CorrelationId
{
    /// <summary>Nombre de la cabecera HTTP.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Longitud máxima aceptada; valores más largos se descartan y se genera uno nuevo.</summary>
    public const int MaxLength = 64;

    /// <summary>Genera un identificador nuevo (16 bytes aleatorios en hexadecimal minúscula).</summary>
    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Valida un valor recibido de un cliente. Se rechaza todo lo que no sea alfanumérico ASCII, punto,
    /// guion o guion bajo para que nunca llegue contenido arbitrario a logs, cabeceras de respuesta ni auditoría.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
