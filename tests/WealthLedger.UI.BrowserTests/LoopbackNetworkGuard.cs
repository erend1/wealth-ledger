using System.Collections.Concurrent;
using System.Net;
using Microsoft.Playwright;

namespace WealthLedger.UI.BrowserTests;

internal sealed class LoopbackNetworkGuard
{
    private readonly ConcurrentQueue<string> _externalRequests = new();

    internal IReadOnlyCollection<string> ExternalRequests =>
        _externalRequests.ToArray();

    internal async Task AttachAsync(IBrowserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.RouteAsync(
            "**/*",
            async route =>
            {
                if (IsExternalNetworkRequest(route.Request.Url))
                {
                    _externalRequests.Enqueue(route.Request.Url);
                    await route.AbortAsync();
                    return;
                }

                await route.ContinueAsync();
            });
    }

    private static bool IsExternalNetworkRequest(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (string.Equals(
                uri.Host,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host, out var address)
               || !IPAddress.IsLoopback(address);
    }
}
