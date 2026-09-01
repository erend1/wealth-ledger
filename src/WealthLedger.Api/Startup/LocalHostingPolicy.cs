using System.Net;
using WealthLedger.Application.LocalData;

namespace WealthLedger.Api.Startup;

internal static class LocalHostingPolicy
{
    internal static LocalDataFailure? Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(configuration["HTTP_PORTS"])
            || !string.IsNullOrWhiteSpace(configuration["HTTPS_PORTS"]))
        {
            return Invalid(
                "Port-only hosting configuration is not allowed because it binds beyond loopback.");
        }

        var urls = new List<string>();
        var configuredUrls = configuration["urls"];

        if (!string.IsNullOrWhiteSpace(configuredUrls))
        {
            urls.AddRange(
                configuredUrls.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
        }

        foreach (var endpoint in configuration
                     .GetSection("Kestrel:Endpoints")
                     .GetChildren())
        {
            var endpointUrl = endpoint["Url"];

            if (!string.IsNullOrWhiteSpace(endpointUrl))
            {
                urls.Add(endpointUrl);
            }
        }

        if (urls.Count == 0)
        {
            return Invalid(
                "At least one explicit loopback API URL is required.");
        }

        if (urls.Any(url => !IsLoopbackUrl(url)))
        {
            return Invalid(
                "API URLs must bind only to localhost, 127.0.0.1, or [::1].");
        }

        var allowedHosts = configuration["AllowedHosts"];

        if (string.IsNullOrWhiteSpace(allowedHosts))
        {
            return Invalid(
                "AllowedHosts must explicitly list loopback host names.");
        }

        var hosts = allowedHosts.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);

        if (hosts.Length == 0 || hosts.Any(host => !IsLoopbackHost(host)))
        {
            return Invalid(
                "AllowedHosts may contain only loopback host names or addresses.");
        }

        return null;
    }

    private static bool IsLoopbackUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return IsLoopbackHost(uri.Host);
    }

    private static bool IsLoopbackHost(string value)
    {
        var host = value.Trim();

        if (host.StartsWith("[", StringComparison.Ordinal)
            && host.EndsWith("]", StringComparison.Ordinal))
        {
            host = host[1..^1];
        }

        if (string.Equals(
                host,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address)
               && IPAddress.IsLoopback(address);
    }

    private static LocalDataFailure Invalid(string message)
        => new(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            message);
}
