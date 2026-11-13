using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PeerPulse.Web.Services;

// Logs actual result execution, including Razor/partial rendering.
public sealed class ReportResultLoggingFilter : IAsyncResultFilter
{
    private readonly ILogger<ReportResultLoggingFilter> _logger;

    public ReportResultLoggingFilter(ILogger<ReportResultLoggingFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        var timer = Stopwatch.StartNew();
        var resultType = context.Result.GetType().Name;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = http.TraceIdentifier
        });
        _logger.LogInformation("Comparison result execution started. TraceId={TraceId}; ResultType={ResultType}",
            http.TraceIdentifier, resultType);
        try
        {
            var executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                _logger.LogError(executed.Exception,
                    "Comparison result rendering failed. TraceId={TraceId}; ResultType={ResultType}; DurationMs={DurationMs}",
                    http.TraceIdentifier, resultType, timer.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "Comparison result execution finished. TraceId={TraceId}; ResultType={ResultType}; StatusCode={StatusCode}; Cancelled={Cancelled}; DurationMs={DurationMs}",
                    http.TraceIdentifier, resultType, http.Response.StatusCode,
                    executed.Canceled, timer.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Comparison result cancelled by client. TraceId={TraceId}", http.TraceIdentifier);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Comparison result execution failed. TraceId={TraceId}; ResultType={ResultType}; DurationMs={DurationMs}",
                http.TraceIdentifier, resultType, timer.ElapsedMilliseconds);
            throw;
        }
    }
}
