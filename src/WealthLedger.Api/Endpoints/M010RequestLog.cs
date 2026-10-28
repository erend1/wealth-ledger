using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using WealthLedger.Api.ErrorHandling;
using WealthLedger.Application.Evidence;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// One privacy-safe operational line per M010 request.
/// </summary>
/// <remarks>
/// The line carries the route template, an outcome category, the status and
/// stable error code, an item count, a size class and the duration. It never
/// carries identities, labels, notes, references, file names, digests,
/// quantities, cursors, idempotency keys, bodies, SQL or stack traces.
/// </remarks>
internal sealed class M010RequestLogFilter : IEndpointFilter
{
    internal const string Category = "WealthLedger.Api.M010";

    private const string ItemCountKey = "WealthLedger.M010.ItemCount";
    private const string SizeClassKey = "WealthLedger.M010.SizeClass";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var result = await next(context);
            var statusCode = (result as IStatusCodeHttpResult)?.StatusCode
                             ?? StatusCodes.Status200OK;
            var errorCode = result is ProblemHttpResult problem
                            && problem.ProblemDetails.Extensions.TryGetValue(
                                "code",
                                out var code)
                ? code as string
                : null;

            Write(httpContext, OutcomeOf(statusCode), statusCode, errorCode, startedAt);
            return result;
        }
        catch (OperationCanceledException)
            when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing failed on the server.
            Write(
                httpContext,
                "CANCELLED",
                StatusCodes.Status499ClientClosedRequest,
                errorCode: null,
                startedAt);
            throw;
        }
        catch (Exception exception)
        {
            var (statusCode, errorCode) = ApiExceptionHandler.Describe(exception);

            // Stored bytes that no longer match their digest are a fault to
            // notice, not a routine refusal, whatever status the caller sees.
            var outcome = exception is EvidenceException
            {
                Category: EvidenceErrorCategory.Integrity
            }
                ? "FAILED"
                : OutcomeOf(statusCode);

            Write(httpContext, outcome, statusCode, errorCode, startedAt);
            throw;
        }
    }

    internal static void SetItemCount(HttpContext httpContext, int itemCount)
        => httpContext.Items[ItemCountKey] = itemCount;

    internal static void SetSizeClass(HttpContext httpContext, long byteLength)
        => httpContext.Items[SizeClassKey] = byteLength switch
        {
            <= 0 => "EMPTY",
            <= 64 * 1024 => "UP_TO_64_KIB",
            <= 1024 * 1024 => "UP_TO_1_MIB",
            <= 10 * 1024 * 1024 => "UP_TO_10_MIB",
            _ => "OVER_10_MIB"
        };

    private static string OutcomeOf(int statusCode)
        => statusCode switch
        {
            < 400 => "SUCCESS",
            < 500 => "REJECTED",
            _ => "FAILED"
        };

    private static void Write(
        HttpContext httpContext,
        string outcome,
        int statusCode,
        string? errorCode,
        long startedAt)
    {
        // The template, never the concrete path: a path carries identities.
        // A group's root route ends in a slash; the log line drops it.
        var route = (httpContext.GetEndpoint() as RouteEndpoint)
                    ?.RoutePattern.RawText?.TrimEnd('/')
                    ?? "UNKNOWN";
        var itemCount = httpContext.Items[ItemCountKey] as int? ?? 0;
        var sizeClass = httpContext.Items[SizeClassKey] as string ?? "NONE";
        var durationMilliseconds = checked(
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(Category);

        logger.Log(
            outcome == "FAILED" ? LogLevel.Warning : LogLevel.Information,
            "M010 request completed for route {Route} with outcome {Outcome}, status {StatusCode}, code {ErrorCode}, item count {ItemCount}, size class {SizeClass}, and duration {DurationMilliseconds} ms.",
            route,
            outcome,
            statusCode,
            errorCode ?? "NONE",
            itemCount,
            sizeClass,
            durationMilliseconds);
    }
}
