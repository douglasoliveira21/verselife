using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LifeVerse.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled request failure; trace {TraceId}", context.TraceIdentifier);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == 400 ? "The request is invalid." : "An unexpected error occurred.",
            Type = status == 400 ? "https://lifeverse.local/problems/invalid-request" : "https://lifeverse.local/problems/internal-error",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
