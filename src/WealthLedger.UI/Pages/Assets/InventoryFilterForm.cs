using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using WealthLedger.Application.Inventory;

namespace WealthLedger.UI.Pages.Assets;

/// <summary>
/// The raw Assets query string, shared by the position and lot pages.
/// </summary>
public sealed class InventoryFilterForm
{
    public string? AsOf { get; set; }

    public string? IncludeZero { get; set; }

    public string? IncludeExhausted { get; set; }

    public string? PortfolioId { get; set; }

    public string? AccountId { get; set; }

    public string? InstitutionId { get; set; }

    public string? AssetId { get; set; }

    public string? PageSize { get; set; }

    public string? Cursor { get; set; }
}

public sealed record InventoryFilterError(string Field, string ResourceKey);

/// <summary>
/// Maps the Assets query string onto the M010 inventory queries.
/// </summary>
public static class InventoryFilterMapper
{
    public const int DefaultLotPageSize = 20;
    public const int MaximumLotPageSize = 100;
    public const string CheckedValue = "true";

    public static bool TryMapPositions(
        InventoryFilterForm form,
        Guid householdId,
        out ListPositionInventoryQuery? query,
        out IReadOnlyList<InventoryFilterError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);

        var found = new List<InventoryFilterError>();
        var scope = ParseScope(form, found);
        var includeZero = ParseFlag(
            form.IncludeZero,
            nameof(form.IncludeZero),
            found);

        errors = found;

        if (found.Count > 0)
        {
            query = null;
            return false;
        }

        query = new ListPositionInventoryQuery(
            householdId,
            scope.PortfolioId,
            scope.AccountId,
            scope.InstitutionId,
            scope.AssetId,
            scope.AsOf,
            includeZero);
        return true;
    }

    public static bool TryMapLots(
        InventoryFilterForm form,
        Guid householdId,
        int pageSize,
        out ListLotInventoryQuery? query,
        out IReadOnlyList<InventoryFilterError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);

        var found = new List<InventoryFilterError>();
        var scope = ParseScope(form, found);
        var includeExhausted = ParseFlag(
            form.IncludeExhausted,
            nameof(form.IncludeExhausted),
            found);

        errors = found;

        if (found.Count > 0)
        {
            query = null;
            return false;
        }

        query = new ListLotInventoryQuery(
            householdId,
            scope.PortfolioId,
            scope.AccountId,
            scope.InstitutionId,
            scope.AssetId,
            scope.AsOf,
            includeExhausted,
            pageSize,
            string.IsNullOrWhiteSpace(form.Cursor) ? null : form.Cursor);
        return true;
    }

    public static bool TryParseLotPageSize(string? value, out int pageSize)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            pageSize = DefaultLotPageSize;
            return true;
        }

        return int.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out pageSize)
               && pageSize is >= 1 and <= MaximumLotPageSize;
    }

    public static bool HasFilters(InventoryFilterForm form)
        => FilterPairs(form).Any();

    /// <summary>
    /// The filters as query pairs, for links that keep the same scope.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> FilterPairs(
        InventoryFilterForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        foreach (var (key, value) in new[]
                 {
                     ("asOf", form.AsOf),
                     ("includeZero", form.IncludeZero),
                     ("includeExhausted", form.IncludeExhausted),
                     ("portfolioId", form.PortfolioId),
                     ("accountId", form.AccountId),
                     ("institutionId", form.InstitutionId),
                     ("assetId", form.AssetId)
                 })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return new(key, value.Trim());
            }
        }
    }

    public static QueryString NextPageQuery(
        InventoryFilterForm form,
        int pageSize,
        string cursor)
    {
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

    private static (
        DateOnly? AsOf,
        Guid? PortfolioId,
        Guid? AccountId,
        Guid? InstitutionId,
        Guid? AssetId) ParseScope(
            InventoryFilterForm form,
            List<InventoryFilterError> errors)
    {
        DateOnly? asOf = null;

        if (!string.IsNullOrWhiteSpace(form.AsOf))
        {
            if (DateOnly.TryParseExact(
                    form.AsOf.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                asOf = parsed;
            }
            else
            {
                errors.Add(
                    new InventoryFilterError(
                        nameof(form.AsOf),
                        "Assets_Validation_Date"));
            }
        }

        return (
            asOf,
            ParseIdentity(form.PortfolioId, nameof(form.PortfolioId), errors),
            ParseIdentity(form.AccountId, nameof(form.AccountId), errors),
            ParseIdentity(form.InstitutionId, nameof(form.InstitutionId), errors),
            ParseIdentity(form.AssetId, nameof(form.AssetId), errors));
    }

    private static bool ParseFlag(
        string? value,
        string field,
        List<InventoryFilterError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value.Trim(), CheckedValue, StringComparison.Ordinal))
        {
            return true;
        }

        errors.Add(new InventoryFilterError(field, "Assets_Validation_Flag"));
        return false;
    }

    private static Guid? ParseIdentity(
        string? value,
        string field,
        List<InventoryFilterError> errors)
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

        errors.Add(new InventoryFilterError(field, "Assets_Validation_Selection"));
        return null;
    }
}
