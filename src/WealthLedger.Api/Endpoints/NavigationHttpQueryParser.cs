using System.Globalization;
using WealthLedger.Application.Navigation;

namespace WealthLedger.Api.Endpoints;

internal static class NavigationHttpQueryParser
{
    internal static bool TryParse(
        HttpRequest request,
        bool allowIncludeInactive,
        out NavigationHttpQuery query,
        out IResult? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageSize = 50;

        if (request.Query.TryGetValue("pageSize", out var pageSizeValues)
            && (pageSizeValues.Count != 1
                || !int.TryParse(
                    pageSizeValues[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out pageSize)
                || pageSize is < 1 or > 100))
        {
            query = default;
            error = Problem(
                NavigationRequestException.PageSizeInvalidCode,
                "Page size must be an integer between 1 and 100.");
            return false;
        }

        var includeInactive = false;

        if (request.Query.TryGetValue(
                "includeInactive",
                out var filterValues))
        {
            if (!allowIncludeInactive
                || filterValues.Count != 1
                || !TryParseBoolean(filterValues[0], out includeInactive))
            {
                query = default;
                error = Problem(
                    NavigationRequestException.FilterInvalidCode,
                    "The includeInactive filter must be true or false when supported.");
                return false;
            }
        }

        string? cursor = null;

        if (request.Query.TryGetValue("cursor", out var cursorValues))
        {
            if (cursorValues.Count != 1)
            {
                query = default;
                error = Problem(
                    NavigationRequestException.CursorInvalidCode,
                    "The navigation cursor is malformed or unsupported.");
                return false;
            }

            cursor = cursorValues[0];
        }

        query = new NavigationHttpQuery(
            pageSize,
            cursor,
            includeInactive);
        error = null;
        return true;
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static IResult Problem(string code, string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid navigation request",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
}

internal readonly record struct NavigationHttpQuery(
    int PageSize,
    string? Cursor,
    bool IncludeInactive);
