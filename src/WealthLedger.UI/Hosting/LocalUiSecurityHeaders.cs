using Microsoft.AspNetCore.Http;

namespace WealthLedger.UI.Hosting;

internal static class LocalUiSecurityHeaders
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; object-src 'none'; base-uri 'self'; "
        + "form-action 'self'; frame-ancestors 'none'";

    internal static void Apply(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
    }
}
