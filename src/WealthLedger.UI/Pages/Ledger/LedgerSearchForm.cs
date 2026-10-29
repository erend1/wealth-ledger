using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Ledger;

/// <summary>
/// The raw Ledger search query string. Every value stays text until the
/// mapper has checked it, so a malformed value becomes a field message rather
/// than a silent model-binding default.
/// </summary>
public sealed class LedgerSearchForm
{
    public string? ExecutedFrom { get; set; }

    public string? ExecutedTo { get; set; }

    public string[]? Type { get; set; }

    public string? PortfolioId { get; set; }

    public string? AccountId { get; set; }

    public string? InstitutionId { get; set; }

    public string? AssetId { get; set; }

    public string? Reference { get; set; }

    public string? Reversal { get; set; }

    public string? PageSize { get; set; }

    public string? Cursor { get; set; }
}

public sealed record LedgerSearchFormError(string Field, string ResourceKey);

/// <summary>
/// Maps the Ledger search query string onto the accepted M010 search query.
/// </summary>
/// <remarks>
/// The status filter is not offered: Decision 5 keeps the ordinary Ledger on
/// effective Posted history, so the query always leaves it to the use case's
/// Posted default.
/// </remarks>
public static class LedgerSearchFormMapper
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int MaximumReferenceLength = 256;

    public const string ReversalAny = "ANY";
    public const string ReversalOriginalOnly = "ORIGINAL_ONLY";
    public const string ReversalReversalOnly = "REVERSAL_ONLY";
    public const string ReversalReversedOriginal = "REVERSED_ORIGINAL";

    public static IReadOnlyList<string> ReversalCodes { get; } =
    [
        ReversalAny,
        ReversalOriginalOnly,
        ReversalReversalOnly,
        ReversalReversedOriginal
    ];

    /// <summary>
    /// Parses the page size, which is navigation state rather than a filter.
    /// </summary>
    public static bool TryParsePageSize(string? value, out int pageSize)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            pageSize = DefaultPageSize;
            return true;
        }

        return int.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out pageSize)
               && pageSize is >= 1 and <= MaximumPageSize;
    }

    public static bool TryMap(
        LedgerSearchForm form,
        Guid householdId,
        int pageSize,
        out SearchLedgerTransactionsQuery? query,
        out IReadOnlyList<LedgerSearchFormError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);

        var found = new List<LedgerSearchFormError>();
        var executedFrom = ParseDate(
            form.ExecutedFrom,
            nameof(form.ExecutedFrom),
            found);
        var executedTo = ParseDate(
            form.ExecutedTo,
            nameof(form.ExecutedTo),
            found);

        if (executedFrom is not null
            && executedTo is not null
            && executedFrom > executedTo)
        {
            found.Add(
                new LedgerSearchFormError(
                    nameof(form.ExecutedTo),
                    "LedgerSearch_Validation_DateRange"));
        }

        var types = ParseTypes(form.Type, found);
        var portfolioId = ParseIdentity(
            form.PortfolioId,
            nameof(form.PortfolioId),
            found);
        var accountId = ParseIdentity(
            form.AccountId,
            nameof(form.AccountId),
            found);
        var institutionId = ParseIdentity(
            form.InstitutionId,
            nameof(form.InstitutionId),
            found);
        var assetId = ParseIdentity(
            form.AssetId,
            nameof(form.AssetId),
            found);
        var reference = form.Reference?.Trim();

        if (reference?.Length > MaximumReferenceLength)
        {
            found.Add(
                new LedgerSearchFormError(
                    nameof(form.Reference),
                    "LedgerSearch_Validation_Reference"));
        }

        var reversal = ParseReversal(form.Reversal, found);

        errors = found;

        if (found.Count > 0)
        {
            query = null;
            return false;
        }

        query = new SearchLedgerTransactionsQuery(
            householdId,
            executedFrom,
            executedTo,
            types,
            Statuses: null,
            assetId,
            institutionId,
            accountId,
            portfolioId,
            string.IsNullOrEmpty(reference) ? null : reference,
            reversal,
            pageSize,
            string.IsNullOrWhiteSpace(form.Cursor) ? null : form.Cursor);
        return true;
    }

    /// <summary>
    /// True when any filter is set; paging state alone does not count.
    /// </summary>
    public static bool HasFilters(LedgerSearchForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        return FilterPairs(form).Any();
    }

    /// <summary>
    /// The query string for the next page: the same filters, so the server
    /// cursor stays bound to the same fingerprint, plus the opaque cursor.
    /// </summary>
    public static QueryString NextPageQuery(
        LedgerSearchForm form,
        int pageSize,
        string cursor)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);

        var builder = new QueryBuilder
        {
            { "pageSize", pageSize.ToString(CultureInfo.InvariantCulture) }
        };

        foreach (var (key, value) in FilterPairs(form))
        {
            builder.Add(key, value);
        }

        builder.Add("cursor", cursor);

        return builder.ToQueryString();
    }

    /// <summary>
    /// The filters as query pairs, for links that keep the same search.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> FilterPairs(
        LedgerSearchForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        foreach (var pair in new[]
                 {
                     ("executedFrom", form.ExecutedFrom),
                     ("executedTo", form.ExecutedTo)
                 })
        {
            if (!string.IsNullOrWhiteSpace(pair.Item2))
            {
                yield return new(pair.Item1, pair.Item2.Trim());
            }
        }

        foreach (var type in form.Type ?? [])
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                yield return new("type", type.Trim());
            }
        }

        foreach (var pair in new[]
                 {
                     ("portfolioId", form.PortfolioId),
                     ("accountId", form.AccountId),
                     ("institutionId", form.InstitutionId),
                     ("assetId", form.AssetId),
                     ("reference", form.Reference)
                 })
        {
            if (!string.IsNullOrWhiteSpace(pair.Item2))
            {
                yield return new(pair.Item1, pair.Item2.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(form.Reversal)
            && !string.Equals(
                form.Reversal.Trim(),
                ReversalAny,
                StringComparison.Ordinal))
        {
            yield return new("reversal", form.Reversal.Trim());
        }
    }

    private static DateOnly? ParseDate(
        string? value,
        string field,
        List<LedgerSearchFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        errors.Add(new LedgerSearchFormError(field, "LedgerSearch_Validation_Date"));
        return null;
    }

    private static IReadOnlyList<TransactionType> ParseTypes(
        string[]? values,
        List<LedgerSearchFormError> errors)
    {
        var types = new List<TransactionType>();

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var code = value.Trim();
            var match = Enum.GetValues<TransactionType>()
                .Where(type => StableCodes.ToCode(type) == code)
                .Cast<TransactionType?>()
                .SingleOrDefault();

            if (match is null)
            {
                errors.Add(
                    new LedgerSearchFormError(
                        nameof(LedgerSearchForm.Type),
                        "LedgerSearch_Validation_Type"));
                return [];
            }

            types.Add(match.Value);
        }

        return types;
    }

    private static Guid? ParseIdentity(
        string? value,
        string field,
        List<LedgerSearchFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParseExact(value.Trim(), "D", out var parsed)
            && parsed != Guid.Empty)
        {
            return parsed;
        }

        errors.Add(
            new LedgerSearchFormError(field, "LedgerSearch_Validation_Selection"));
        return null;
    }

    private static LedgerSearchReversalRelationship ParseReversal(
        string? value,
        List<LedgerSearchFormError> errors)
    {
        switch (value?.Trim())
        {
            case null:
            case "":
            case ReversalAny:
                return LedgerSearchReversalRelationship.Any;
            case ReversalOriginalOnly:
                return LedgerSearchReversalRelationship.OriginalOnly;
            case ReversalReversalOnly:
                return LedgerSearchReversalRelationship.ReversalOnly;
            case ReversalReversedOriginal:
                return LedgerSearchReversalRelationship.ReversedOriginal;
            default:
                errors.Add(
                    new LedgerSearchFormError(
                        nameof(LedgerSearchForm.Reversal),
                        "LedgerSearch_Validation_Reversal"));
                return LedgerSearchReversalRelationship.Any;
        }
    }
}
