using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenTelemetryDashboard.Host.ErrorHandling;

/// <summary>
/// Catches anything that escapes the endpoints and writes a uniform
/// RFC 7807 <see cref="ProblemDetails"/> response, so HTTP clients see the
/// same error shape they get from validation/concurrency failures. Without
/// this, unhandled exceptions fall through to ASP.NET's default error
/// middleware — HTML in Development and an opaque 500 in Production —
/// which breaks any client doing JSON error parsing.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Client disconnects mid-request raise OperationCanceledException
        // tied to RequestAborted — that isn't a server fault and shouldn't
        // be reshaped as 500. Returning false lets the default behaviour
        // tear the response down (no body, connection closed).
        if (exception is OperationCanceledException &&
            httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        _logger.UnhandledException(
            exception,
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Instance = httpContext.Request.Path,
        };

        // TraceIdentifier lets an SRE find the request in the logs without
        // leaking the exception details to the caller.
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (_environment.IsDevelopment())
        {
            problem.Detail = exception.Message;
            problem.Extensions["exceptionType"] = exception.GetType().FullName;
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        }).ConfigureAwait(false);
    }
}

internal static partial class GlobalExceptionHandlerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Unhandled exception while processing {Method} {Path}")]
    public static partial void UnhandledException(
        this ILogger logger,
        Exception exception,
        string method,
        string path);
}
