using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

internal sealed record PhysicalGoldReviewedPlan(
    IReadOnlyList<PhysicalGoldSelectedLot> Selections,
    IReadOnlyList<PhysicalGoldPlanLine> Lines,
    decimal FineWeightGrams);

internal static class PhysicalGoldPlanBuilder
{
    internal static PhysicalGoldReviewedPlan Build(
        Guid goldAssetId,
        Quantity requestedGrossWeight,
        int requestedPieceCount,
        IReadOnlyCollection<PhysicalGoldSelectedLot>? selections,
        IReadOnlyList<PhysicalGoldCustodyLot> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (selections is null || selections.Count == 0)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.SelectedLotInvalid,
                "Select at least one physical-gold lot.");
        }

        var ordered = PhysicalGoldCanonicalizer.OrderSelections(selections);
        var candidatesById = candidates.ToDictionary(x => x.AssetLotId);
        var seen = new HashSet<Guid>();
        var lines = new List<PhysicalGoldPlanLine>(ordered.Count);
        long totalGross = 0;
        var totalPieces = 0;
        decimal fineWeight = 0;

        try
        {
            foreach (var selection in ordered)
            {
                if (selection.AssetLotId == Guid.Empty
                    || selection.GrossWeight.RawE8 <= 0
                    || selection.PieceCount <= 0)
                {
                    throw PhysicalGoldException.Invalid(
                        PhysicalGoldErrorCodes.SelectedLotInvalid,
                        "Every selected lot requires an identity, positive gross weight, and positive whole-piece count.");
                }

                if (!seen.Add(selection.AssetLotId))
                {
                    throw PhysicalGoldException.Invalid(
                        PhysicalGoldErrorCodes.DuplicateSelection,
                        "A physical-gold plan cannot select the same lot twice.");
                }

                if (!candidatesById.TryGetValue(
                        selection.AssetLotId,
                        out var candidate)
                    || candidate.GoldAssetId != goldAssetId)
                {
                    throw PhysicalGoldException.Invalid(
                        PhysicalGoldErrorCodes.SelectedLotInvalid,
                        "A selected lot does not belong to the chosen gold custody scope and asset.");
                }

                if (selection.GrossWeight.RawE8
                    > candidate.ScopedGrossWeight.RawE8)
                {
                    throw PhysicalGoldException.Invalid(
                        PhysicalGoldErrorCodes.InsufficientGrossWeight,
                        "A selected custody lot does not contain the entered gross weight.");
                }

                if (selection.PieceCount > candidate.ScopedPieceCount)
                {
                    throw PhysicalGoldException.Invalid(
                        PhysicalGoldErrorCodes.InsufficientPieces,
                        "A selected custody lot does not contain the entered whole-piece count.");
                }

                if (selection.GrossWeight.RawE8
                        > candidate.GlobalGrossWeight.RawE8
                    || selection.PieceCount > candidate.GlobalPieceCount)
                {
                    throw PhysicalGoldException.Conflict(
                        PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                        "Persisted custody exceeds the lot's global physical-gold balance.");
                }

                var movedFine = checked(
                    (decimal)selection.GrossWeight.RawE8
                    * candidate.Fineness.Ppm)
                    / Quantity.Scale
                    / Fineness.MaximumPpm;

                totalGross = checked(
                    totalGross + selection.GrossWeight.RawE8);
                totalPieces = checked(totalPieces + selection.PieceCount);
                fineWeight = checked(fineWeight + movedFine);

                lines.Add(
                    new PhysicalGoldPlanLine(
                        candidate.AssetLotId,
                        candidate.AcquiredOn,
                        candidate.ScopedGrossWeight.RawE8,
                        candidate.ScopedPieceCount,
                        selection.GrossWeight.RawE8,
                        selection.PieceCount,
                        checked(candidate.ScopedGrossWeight.RawE8
                                - selection.GrossWeight.RawE8),
                        checked(candidate.ScopedPieceCount
                                - selection.PieceCount),
                        candidate.Fineness.Ppm,
                        movedFine,
                        candidate.CostBasis.Status,
                        candidate.CostBasis.Amount?.MinorUnits,
                        candidate.CostBasis.Amount?.Currency.Value,
                        candidate.Hallmark,
                        candidate.CertificateReference));
            }
        }
        catch (OverflowException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PrecisionOverflow,
                "The selected physical-gold plan exceeds the supported exact range.",
                innerException: exception);
        }

        if (totalGross != requestedGrossWeight.RawE8
            || totalPieces != requestedPieceCount)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.SelectedTotalsMismatch,
                "Selected lot gross weight and pieces must reconcile exactly to the requested totals.");
        }

        return new PhysicalGoldReviewedPlan(ordered, lines, fineWeight);
    }

    internal static async Task<PhysicalGoldRealizedCostProjection>
        ProjectRealizedCostAsync(
            Guid householdId,
            PhysicalGoldReviewedPlan plan,
            IPhysicalGoldRealizedCostReadStore realizedCostStore,
            CancellationToken cancellationToken)
    {
        var histories = await realizedCostStore.ListLotHistoryAsync(
            householdId,
            plan.Selections.Select(x => x.AssetLotId).ToArray(),
            cancellationToken);
        var byId = histories.ToDictionary(x => x.AssetLotId);
        var planned = new List<PlannedLotDisposal>(plan.Selections.Count);

        foreach (var selection in plan.Selections)
        {
            if (!byId.TryGetValue(selection.AssetLotId, out var history))
            {
                throw PhysicalGoldException.Conflict(
                    PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                    "A selected lot is missing its persisted acquisition history.");
            }

            planned.Add(new PlannedLotDisposal(
                history,
                selection.GrossWeight));
        }

        RealizedSaleCost result;
        try
        {
            result = RealizedLotCostCalculator.Project(planned);
        }
        catch (Domain.Common.DomainRuleViolationException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Conflict,
                PhysicalGoldErrorCodes.UnsupportedPersistedShape,
                "Persisted physical-gold history cannot support realized-cost derivation.",
                innerException: exception);
        }

        return new PhysicalGoldRealizedCostProjection(
            result.Completeness,
            result.KnownQuantity.RawE8,
            result.UnknownQuantity.RawE8,
            result.KnownAmountsByCurrency
                .Select(x => new PhysicalGoldRealizedCostAmount(
                    x.Currency.Value,
                    x.MinorUnits))
                .ToArray(),
            result.MethodCode);
    }
}

internal static class PhysicalGoldPlanFingerprint
{
    internal const string AlgorithmCode = "SHA256";
    internal const int Version = 1;

    internal static string ComputeSale(
        PhysicalGoldTradeScope scope,
        long requestedGrossWeightRawE8,
        int requestedPieceCount,
        IReadOnlyCollection<PhysicalGoldSelectedLot> plan)
        => Compute(
            LedgerOperationCodes.RecordPhysicalGoldSale,
            scope.HouseholdId,
            scope.PortfolioId,
            scope.GoldAccountId,
            null,
            null,
            scope.GoldAssetId,
            requestedGrossWeightRawE8,
            requestedPieceCount,
            plan);

    internal static string ComputeTransfer(
        PhysicalGoldTransferScope scope,
        long requestedGrossWeightRawE8,
        int requestedPieceCount,
        IReadOnlyCollection<PhysicalGoldSelectedLot> plan)
        => Compute(
            LedgerOperationCodes.RecordPhysicalGoldTransfer,
            scope.HouseholdId,
            scope.SourcePortfolioId,
            scope.SourceGoldAccountId,
            scope.DestinationPortfolioId,
            scope.DestinationGoldAccountId,
            scope.GoldAssetId,
            requestedGrossWeightRawE8,
            requestedPieceCount,
            plan);

    internal static bool Matches(string? carried, string expected)
        => !string.IsNullOrWhiteSpace(carried)
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(carried),
               Encoding.UTF8.GetBytes(expected));

    private static string Compute(
        string operation,
        Guid householdId,
        Guid sourcePortfolioId,
        Guid sourceAccountId,
        Guid? destinationPortfolioId,
        Guid? destinationAccountId,
        Guid goldAssetId,
        long requestedGrossWeightRawE8,
        int requestedPieceCount,
        IReadOnlyCollection<PhysicalGoldSelectedLot> plan)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("operation", operation);
            writer.WriteString("householdId", householdId.ToString("D"));
            writer.WriteString(
                "sourcePortfolioId", sourcePortfolioId.ToString("D"));
            writer.WriteString(
                "sourceAccountId", sourceAccountId.ToString("D"));
            WriteNullableGuid(
                writer, "destinationPortfolioId", destinationPortfolioId);
            WriteNullableGuid(
                writer, "destinationAccountId", destinationAccountId);
            writer.WriteString("goldAssetId", goldAssetId.ToString("D"));
            writer.WriteNumber(
                "requestedGrossWeightRawE8", requestedGrossWeightRawE8);
            writer.WriteNumber("requestedPieceCount", requestedPieceCount);
            writer.WriteStartArray("plan");
            foreach (var line in PhysicalGoldCanonicalizer.OrderSelections(plan))
            {
                writer.WriteStartObject();
                writer.WriteString(
                    "assetLotId", line.AssetLotId.ToString("D"));
                writer.WriteNumber(
                    "grossWeightRawE8", line.GrossWeight.RawE8);
                writer.WriteNumber("pieceCount", line.PieceCount);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlgorithmCode.ToLowerInvariant()}-{Version}-{Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant()}");
    }

    private static void WriteNullableGuid(
        Utf8JsonWriter writer,
        string name,
        Guid? value)
    {
        writer.WritePropertyName(name);
        if (value is Guid id)
        {
            writer.WriteStringValue(id.ToString("D"));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
