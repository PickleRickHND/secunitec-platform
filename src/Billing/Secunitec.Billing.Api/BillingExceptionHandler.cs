using Microsoft.AspNetCore.Diagnostics;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Api;

public sealed class BillingExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status = exception switch
        {
            BillingAccessException => StatusCodes.Status403Forbidden,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            BillingRuleException => StatusCodes.Status400BadRequest,
            _ => 0
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        await Results.Problem(statusCode: status, detail: exception.Message).ExecuteAsync(httpContext);
        return true;
    }
}
