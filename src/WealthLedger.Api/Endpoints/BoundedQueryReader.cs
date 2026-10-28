using System.Globalization;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// The stable codes one M010 route area answers malformed input with.
/// </summary>
internal sealed record BoundedRequestProblems(
    string Title,
    string RequestInvalidCode,
    string PageSizeInvalidCode,
    string CursorInvalidCode);

/// <summary>
/// Strict query-string reading for the M010 routes.
/// </summary>
/// <remarks>
/// Every parameter is optional and single-valued unless read as a list, and
/// an unsupported parameter is refused rather than ignored, so a mistyped
/// filter can never silently widen a result. Malformed input fails before
/// any query runs, and messages name the rule only, never the value.
/// </remarks>
internal sealed class BoundedQueryReader
{
    private const int MaximumListValues = 32;

    private readonly HttpRequest _request;
    private readonly BoundedRequestProblems _problems;
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);

    internal BoundedQueryReader(
        HttpRequest request,
        BoundedRequestProblems problems)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _problems = problems ?? throw new ArgumentNullException(nameof(problems));
    }

    internal IResult? Error { get; private set; }

    internal bool TryRouteIdentity(Guid value)
        => value != Guid.Empty
           || Fail(
               _problems.RequestInvalidCode,
               "A route identity must not be empty.");

    internal bool TryOptionalGuid(string name, out Guid? value)
    {
        value = null;

        if (!TryOptionalSingle(name, out var text))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (!Guid.TryParseExact(text, "D", out var parsed)
            || parsed == Guid.Empty)
        {
            return Fail(
                _problems.RequestInvalidCode,
                "An identity filter must be a non-empty identity.");
        }

        value = parsed;
        return true;
    }

    internal bool TryOptionalDate(string name, out DateOnly? value)
    {
        value = null;

        if (!TryOptionalSingle(name, out var text))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return Fail(
                _problems.RequestInvalidCode,
                "A date filter must be a YYYY-MM-DD date.");
        }

        value = parsed;
        return true;
    }

    internal bool TryOptionalBoolean(string name, out bool value)
    {
        value = false;

        if (!TryOptionalSingle(name, out var text))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        return string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
               || Fail(
                   _problems.RequestInvalidCode,
                   "A boolean filter must be true or false.");
    }

    internal bool TryOptionalText(string name, out string? value)
        => TryOptionalSingle(name, out value);

    internal bool TryOptionalCode<T>(
        string name,
        IReadOnlyDictionary<string, T> codes,
        out T? value)
        where T : struct
    {
        value = null;

        if (!TryOptionalSingle(name, out var text))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (!codes.TryGetValue(text, out var parsed))
        {
            return Fail(
                _problems.RequestInvalidCode,
                "A code filter contains an unsupported code.");
        }

        value = parsed;
        return true;
    }

    internal bool TryCodeList<T>(
        string name,
        IReadOnlyDictionary<string, T> codes,
        out IReadOnlyList<T> values)
    {
        values = [];
        _known.Add(name);

        if (!_request.Query.TryGetValue(name, out var texts))
        {
            return true;
        }

        if (texts.Count > MaximumListValues)
        {
            return Fail(
                _problems.RequestInvalidCode,
                "A code filter repeats too many values.");
        }

        var parsed = new List<T>(texts.Count);

        foreach (var text in texts)
        {
            if (text is null || !codes.TryGetValue(text, out var code))
            {
                return Fail(
                    _problems.RequestInvalidCode,
                    "A code filter contains an unsupported code.");
            }

            parsed.Add(code);
        }

        values = parsed;
        return true;
    }

    internal bool TryPageSize(out int pageSize)
    {
        pageSize = 50;

        if (!TryOptionalSingle("pageSize", out var text))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        return (int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out pageSize)
                && pageSize is >= 1 and <= 100)
               || Fail(
                   _problems.PageSizeInvalidCode,
                   "Page size must be an integer between 1 and 100.");
    }

    internal bool TryCursor(out string? cursor)
    {
        _known.Add("cursor");
        cursor = null;

        if (!_request.Query.TryGetValue("cursor", out var values))
        {
            return true;
        }

        if (values.Count != 1)
        {
            return Fail(
                _problems.CursorInvalidCode,
                "The cursor is malformed or unsupported.");
        }

        cursor = values[0];
        return true;
    }

    /// <summary>
    /// Refuses any parameter the route did not read.
    /// </summary>
    internal bool TryRejectUnsupported()
        => _request.Query.Keys.All(_known.Contains)
           || Fail(
               _problems.RequestInvalidCode,
               "The request contains an unsupported query parameter.");

    private bool TryOptionalSingle(string name, out string? value)
    {
        _known.Add(name);
        value = null;

        if (!_request.Query.TryGetValue(name, out var values))
        {
            return true;
        }

        if (values.Count != 1)
        {
            return Fail(
                _problems.RequestInvalidCode,
                "A single-valued query parameter was repeated.");
        }

        value = values[0];
        return true;
    }

    private bool Fail(string code, string detail)
    {
        Error = BoundedProblems.BadRequest(_problems, code, detail);
        return false;
    }
}

internal static class BoundedProblems
{
    internal static IResult BadRequest(
        BoundedRequestProblems problems,
        string code,
        string detail)
        => Problem(StatusCodes.Status400BadRequest, problems.Title, code, detail);

    internal static IResult Problem(
        int statusCode,
        string title,
        string code,
        string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
}
