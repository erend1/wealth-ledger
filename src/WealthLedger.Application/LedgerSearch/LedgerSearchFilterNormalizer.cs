using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.LedgerSearch;

internal static class LedgerSearchFilterNormalizer
{
    private const int MaximumExternalReferenceLength = 256;

    internal static LedgerSearchNormalizedFilters Normalize(
        SearchLedgerTransactionsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HouseholdId == Guid.Empty)
        {
            throw InvalidFilter("The household identity is required.");
        }

        if (query.ExecutedFrom is not null
            && query.ExecutedTo is not null
            && query.ExecutedFrom > query.ExecutedTo)
        {
            throw InvalidFilter(
                "The execution-date range is invalid.");
        }

        EnsureOptionalGuid(query.AssetId);
        EnsureOptionalGuid(query.InstitutionId);
        EnsureOptionalGuid(query.AccountId);
        EnsureOptionalGuid(query.PortfolioId);

        if (!Enum.IsDefined(query.ReversalRelationship))
        {
            throw InvalidFilter(
                "The reversal-relationship filter is invalid.");
        }

        var types = NormalizeTypes(query.Types);
        var statuses = NormalizeStatuses(query.Statuses);
        var externalReference = NormalizeExternalReference(
            query.ExternalReferenceContains);
        var fingerprint = ComputeFingerprint(
            query.ExecutedFrom,
            query.ExecutedTo,
            types,
            statuses,
            query.AssetId,
            query.InstitutionId,
            query.AccountId,
            query.PortfolioId,
            externalReference,
            query.ReversalRelationship);

        return new LedgerSearchNormalizedFilters(
            query.ExecutedFrom,
            query.ExecutedTo,
            types,
            statuses,
            query.AssetId,
            query.InstitutionId,
            query.AccountId,
            query.PortfolioId,
            externalReference,
            query.ReversalRelationship,
            fingerprint);
    }

    private static IReadOnlyList<TransactionType> NormalizeTypes(
        IReadOnlyList<TransactionType>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        if (values.Any(value => !Enum.IsDefined(value)))
        {
            throw InvalidFilter("A transaction-type filter is invalid.");
        }

        return values
            .Distinct()
            .OrderBy(value => (int)value)
            .ToArray();
    }

    private static IReadOnlyList<TransactionStatus> NormalizeStatuses(
        IReadOnlyList<TransactionStatus>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [TransactionStatus.Posted];
        }

        if (values.Any(value => !Enum.IsDefined(value))
            || values.Any(value => value != TransactionStatus.Posted))
        {
            throw InvalidFilter(
                "The normal ledger search supports effective Posted history only.");
        }

        return [TransactionStatus.Posted];
    }

    private static string? NormalizeExternalReference(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > MaximumExternalReferenceLength)
        {
            throw InvalidFilter(
                "The external-reference search text is too long.");
        }

        return normalized.ToUpperInvariant();
    }

    private static void EnsureOptionalGuid(Guid? value)
    {
        if (value == Guid.Empty)
        {
            throw InvalidFilter("A filter identity is invalid.");
        }
    }

    private static string ComputeFingerprint(
        DateOnly? executedFrom,
        DateOnly? executedTo,
        IReadOnlyList<TransactionType> types,
        IReadOnlyList<TransactionStatus> statuses,
        Guid? assetId,
        Guid? institutionId,
        Guid? accountId,
        Guid? portfolioId,
        string? externalReference,
        LedgerSearchReversalRelationship reversalRelationship)
    {
        var canonical = string.Join(
            '|',
            FormatDate(executedFrom),
            FormatDate(executedTo),
            string.Join(',', types.Select(value => ((int)value).ToString(CultureInfo.InvariantCulture))),
            string.Join(',', statuses.Select(value => ((int)value).ToString(CultureInfo.InvariantCulture))),
            FormatGuid(assetId),
            FormatGuid(institutionId),
            FormatGuid(accountId),
            FormatGuid(portfolioId),
            externalReference ?? string.Empty,
            ((int)reversalRelationship).ToString(CultureInfo.InvariantCulture));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
           ?? string.Empty;

    private static string FormatGuid(Guid? value)
        => value?.ToString("D") ?? string.Empty;

    private static LedgerSearchRequestException InvalidFilter(string message)
        => new(
            LedgerSearchRequestException.FilterInvalidCode,
            message);
}
