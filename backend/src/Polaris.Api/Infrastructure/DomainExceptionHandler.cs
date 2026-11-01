using Microsoft.AspNetCore.Diagnostics;
using Polaris.Domain.Common;

namespace Polaris.Api.Infrastructure;

/// <summary>Convierte reglas de negocio violadas en ProblemDetails (RFC 9457) con un "code" estable.</summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain) return false;

        var status = domain.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        var result = Results.Problem(
            statusCode: status,
            title: domain.Message,
            type: $"https://polaris.local/errors/{domain.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = domain.Code });

        await result.ExecuteAsync(httpContext);
        return true;
    }
}
