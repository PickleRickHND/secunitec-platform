using Microsoft.AspNetCore.Diagnostics;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Api;

/// <summary>
/// Traduce los errores de Billing a ProblemDetails con un <c>code</c> estable (etapa 1.2) y sin detalles internos
/// (A05). R18: ningún error esperado llega como 500.
/// </summary>
public sealed class BillingExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        IResult? result = exception switch
        {
            BillingValidationException validation => Results.ValidationProblem(
                validation.Errors.ToDictionary(x => x.Key, x => x.Value),
                detail: validation.Message,
                extensions: Code("validacion")),
            BillingRuleException rule => Problem(StatusCodes.Status400BadRequest, rule.Message, rule.Code),
            BillingAccessException access => Problem(StatusCodes.Status403Forbidden, access.Message, "acceso.denegado"),
            BillingConflictException conflict => Problem(StatusCodes.Status409Conflict, conflict.Message, "conflicto"),
            KeyNotFoundException notFound => Problem(StatusCodes.Status404NotFound, notFound.Message, "no_encontrado"),
            _ => null,
        };

        if (result is null)
        {
            return false;
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }

    private static IResult Problem(int status, string detail, string code) =>
        Results.Problem(statusCode: status, detail: detail, extensions: Code(code));

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };
}
