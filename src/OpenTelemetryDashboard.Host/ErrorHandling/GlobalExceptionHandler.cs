using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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

        // BadHttpRequestException covers framework-side binding failures
        // (missing required query/route values, malformed JSON bodies,
        // request-too-large, etc.). Its StatusCode is the right one to
        // surface — usually 400 — so we honour it instead of clobbering
        // every binding miss as a server-side 500.
        var statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        // 5xx is a server fault worth logging at Error; 4xx is the caller's
        // fault and gets a quieter Debug entry — otherwise every malformed
        // request would page the on-call SRE.
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.ToString();
        if (statusCode >= 500)
        {
            _logger.UnhandledServerException(exception, method, path);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.RejectedClientRequest(exception.Message, method, path);
        }

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode >= 500
                ? "An unexpected error occurred."
                : "The request could not be processed.",
            Type = $"https://httpstatuses.io/{statusCode}",
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
        else if (statusCode < 500)
        {
            // 4xx Detail is safe to expose: it's the framework's "what was
            // wrong with your request" string (e.g. "Required parameter
            // 'rowVersion' was not provided"), not internal state.
            problem.Detail = exception.Message;
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
        Message = "Unhandled server exception while processing {Method} {Path}")]
    public static partial void UnhandledServerException(
        this ILogger logger,
        Exception exception,
        string method,
        string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Rejected client request {Method} {Path}: {Reason}")]
    public static partial void RejectedClientRequest(
        this ILogger logger,
        string reason,
        string method,
        string path);
}
