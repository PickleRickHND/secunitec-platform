namespace Secunitec.Billing.Application;

/// <summary>El usuario no puede hacer esta operación o el recurso no está a su alcance; la API responde 403.</summary>
public sealed class BillingAccessException(string message) : Exception(message);

/// <summary>El recurso ya existe (RTN repetido, obligado duplicado o alta concurrente); la API responde 409.</summary>
public sealed class BillingConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>La petición no pasó la validación; la API responde 400 con los errores por campo.</summary>
public sealed class BillingValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("La petición tiene datos inválidos.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
