using Microsoft.AspNetCore.Http;

namespace WealthLedger.UI.Hosting;

internal sealed class LocalStartupPageAccessMiddleware
{
    private readonly RequestDelegate _next;

    public LocalStartupPageAccessMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        LocalUiStartupContext startupContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(startupContext);

        var access = context.GetEndpoint()?.Metadata
            .GetMetadata<LocalStartupPageAttribute>();

        if (access is null)
        {
            await _next(context);
            return;
        }

        LocalUiSecurityHeaders.Apply(context.Response.Headers);

        if (!access.Allows(startupContext.Mode))
        {
            context.Response.StatusCode =
                StatusCodes.Status404NotFound;
            return;
        }

        if (!IsAllowedMethod(context.Request.Method, access.SupportsPost))
        {
            context.Response.Headers.Allow = "GET, HEAD";
            context.Response.StatusCode =
                StatusCodes.Status405MethodNotAllowed;
            return;
        }

        await _next(context);
    }

    private static bool IsAllowedMethod(
        string method,
        bool supportsPost)
        => HttpMethods.IsGet(method)
           || HttpMethods.IsHead(method)
           || (supportsPost && HttpMethods.IsPost(method));
}
