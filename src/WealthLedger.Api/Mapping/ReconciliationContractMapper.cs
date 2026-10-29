using WealthLedger.Api.Contracts;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Api.Mapping;

internal static class ReconciliationContractMapper
{
    internal static readonly IReadOnlyDictionary<string, ReconciliationResolutionKind> KindCodes =
        Enum.GetValues<ReconciliationResolutionKind>()
            .ToDictionary(ToCode, StringComparer.Ordinal);

    internal static string RunLocation(Guid householdId, Guid runId)
        => $"/api/households/{householdId}/reconciliations/{runId}";

    internal static ReconciliationRunSummaryResponse ToResponse(
        this ReconciliationRunSummary run)
        => new(
            run.ReconciliationRunId,
            run.HouseholdId,
            run.EvidenceId,
            run.EvidenceEffectiveDate,
            run.RunAtUtc,
            ToCode(run.Method),
            run.MethodVersion,
            run.ItemCount,
            run.MatchedCount,
            run.OutOfToleranceCount,
            run.OpenCount,
            run.CorrectedByTransactionCount,
            run.DocumentedExceptionCount);

    internal static ReconciliationRunDetailResponse ToResponse(
        this ReconciliationRunDetail run)
        => new(
            run.ReconciliationRunId,
            run.HouseholdId,
            run.EvidenceId,
            run.EvidenceEffectiveDate,
            run.RunAtUtc,
            ToCode(run.Method),
            run.MethodVersion,
            run.IsStale,
            run.Items
                .Select(
                    item => new ReconciliationItemResponse(
                        item.ReconciliationItemId,
                        item.Sequence,
                        item.ObservationId,
                        item.Scope.ToResponse(),
                        item.ObservedQuantityRawE8,
                        item.LedgerQuantityRawE8,
                        item.DeltaRawE8,
                        item.ToleranceRawE8,
                        item.ObservedPieceCount,
                        item.LedgerPieceCount,
                        item.PieceDelta,
                        ToCode(item.ComparisonStatus),
                        item.ResolutionState is null
                            ? null
                            : ToCode(item.ResolutionState.Value),
                        item.CorrectiveTransactionId,
                        item.Explanation,
                        item.ResolvedAtUtc,
                        item.CurrentLedgerQuantityRawE8,
                        item.CurrentLedgerPieceCount,
                        item.IsStale))
                .ToArray());

    internal static string ToCode(ReconciliationResolutionKind value)
        => value switch
        {
            ReconciliationResolutionKind.CorrectedByTransaction => "CORRECTED_BY_TRANSACTION",
            ReconciliationResolutionKind.DocumentedException => "DOCUMENTED_EXCEPTION",
            _ => throw Unsupported(value)
        };

    internal static string ToCode(ReconciliationMethod value)
        => value switch
        {
            ReconciliationMethod.ExactScopeQuantityAsOf => "EXACT_SCOPE_QUANTITY_AS_OF",
            _ => throw Unsupported(value)
        };

    internal static string ToCode(ReconciliationComparisonStatus value)
        => value switch
        {
            ReconciliationComparisonStatus.Matched => "MATCHED",
            ReconciliationComparisonStatus.OutOfTolerance => "OUT_OF_TOLERANCE",
            _ => throw Unsupported(value)
        };

    internal static string ToCode(ReconciliationResolutionState value)
        => value switch
        {
            ReconciliationResolutionState.Open => "OPEN",
            ReconciliationResolutionState.CorrectedByTransaction => "CORRECTED_BY_TRANSACTION",
            ReconciliationResolutionState.DocumentedException => "DOCUMENTED_EXCEPTION",
            _ => throw Unsupported(value)
        };

    private static ArgumentOutOfRangeException Unsupported<T>(T value)
        => new(
            nameof(value),
            value,
            "The value has no stable API code.");
}
