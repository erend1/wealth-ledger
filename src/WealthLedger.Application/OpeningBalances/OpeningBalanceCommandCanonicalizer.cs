using System.Globalization;
using System.Text.Json;
using WealthLedger.Domain.Lots;

namespace WealthLedger.Application.OpeningBalances;

internal static class OpeningBalanceCommandCanonicalizer
{
    internal static RecordOpeningBalanceCommand Normalize(
        RecordOpeningBalanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var note = NormalizeRequiredText(
            command.Note,
            2_000,
            "An opening balance requires a source note.");

        var externalReference = NormalizeOptionalText(
            command.ExternalReference,
            256);

        if (command.Lots is null)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.LotsRequired,
                "The opening-balance lot collection is required.");
        }

        var lots = new OpeningBalanceLotCommand[command.Lots.Count];
        var keys = new HashSet<OpeningBalanceLotCanonicalKey>();

        for (var index = 0; index < command.Lots.Count; index++)
        {
            var lot = command.Lots[index]
                ?? throw Invalid(
                    OpeningBalanceErrorCodes.LotsRequired,
                    "An opening-balance lot cannot be null.");

            ArgumentNullException.ThrowIfNull(lot.CostBasis);

            var normalized = lot with
            {
                PhysicalGoldDetail = NormalizeGoldDetail(
                    lot.PhysicalGoldDetail)
            };

            var key = OpeningBalanceLotCanonicalKey.From(normalized);

            if (!keys.Add(key))
            {
                throw Invalid(
                    OpeningBalanceErrorCodes.DuplicateLot,
                    "Economically indistinguishable opening lots must be grouped.");
            }

            lots[index] = normalized;
        }

        return command with
        {
            ExternalReference = externalReference,
            Note = note,
            Lots = lots
        };
    }

    internal static IReadOnlyList<OpeningBalanceLotCommand> OrderLots(
        IReadOnlyList<OpeningBalanceLotCommand> lots)
        => lots
            .Select(lot => new
            {
                Lot = lot,
                SortKey = JsonSerializer.Serialize(
                    OpeningBalanceLotCanonicalKey.From(lot))
            })
            .OrderBy(item => item.SortKey, StringComparer.Ordinal)
            .Select(item => item.Lot)
            .ToArray();

    internal static string ToCostBasisStatusCode(CostBasisStatus status)
        => status switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            CostBasisStatus.NotApplicable => "NOT_APPLICABLE",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported cost-basis status.")
        };

    private static PhysicalGoldLotDetail? NormalizeGoldDetail(
        PhysicalGoldLotDetail? detail)
        => detail is null
            ? null
            : new PhysicalGoldLotDetail(
                detail.Fineness,
                detail.PieceCount,
                detail.Hallmark,
                detail.CertificateReference,
                detail.Note);

    private static string NormalizeRequiredText(
        string? value,
        int maximumLength,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(
                OpeningBalanceErrorCodes.SourceTextInvalid,
                missingMessage);
        }

        return NormalizeText(value, maximumLength);
    }

    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeText(value, maximumLength);

    private static string NormalizeText(
        string value,
        int maximumLength)
    {
        var normalized = value.Trim();

        if (normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            throw Invalid(
                OpeningBalanceErrorCodes.SourceTextInvalid,
                "Opening-balance source text is invalid.");
        }

        return normalized;
    }

    private static OpeningBalanceException Invalid(
        string errorCode,
        string message)
        => new(
            OpeningBalanceErrorCategory.Validation,
            errorCode,
            message);
}

internal sealed record OpeningBalanceLotCanonicalKey(
    long QuantityRawE8,
    string? AcquiredOn,
    string CostBasisStatusCode,
    long? OriginalCostBasisMinorUnits,
    string? CostBasisCurrencyCode,
    int? FinenessPartsPerMillion,
    int? PieceCount,
    string? Hallmark,
    string? CertificateReference,
    string? Note)
{
    internal static OpeningBalanceLotCanonicalKey From(
        OpeningBalanceLotCommand lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(lot.CostBasis);

        return new OpeningBalanceLotCanonicalKey(
            lot.Quantity.RawE8,
            lot.AcquiredOn?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture),
            OpeningBalanceCommandCanonicalizer.ToCostBasisStatusCode(
                lot.CostBasis.Status),
            lot.CostBasis.Amount?.MinorUnits,
            lot.CostBasis.Amount?.Currency.Value,
            lot.PhysicalGoldDetail?.Fineness.Ppm,
            lot.PhysicalGoldDetail?.PieceCount,
            lot.PhysicalGoldDetail?.Hallmark,
            lot.PhysicalGoldDetail?.CertificateReference,
            lot.PhysicalGoldDetail?.Note);
    }
}
