namespace WealthLedger.Application.OpeningBalances;

public sealed class PreviewOpeningBalanceUseCase
{
    private readonly IOpeningBalanceReferenceReadStore _referenceStore;
    private readonly IOpeningBalanceEffectiveHistoryReadStore _historyStore;
    private readonly TimeProvider _timeProvider;

    public PreviewOpeningBalanceUseCase(
        IOpeningBalanceReferenceReadStore referenceStore,
        IOpeningBalanceEffectiveHistoryReadStore historyStore,
        TimeProvider timeProvider)
    {
        _referenceStore = referenceStore
            ?? throw new ArgumentNullException(nameof(referenceStore));
        _historyStore = historyStore
            ?? throw new ArgumentNullException(nameof(historyStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OpeningBalancePreview> ExecuteAsync(
        RecordOpeningBalanceCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalized =
            OpeningBalanceCommandCanonicalizer.Normalize(command);
        var evaluatedAtUtc = _timeProvider.GetUtcNow();

        var validated =
            await OpeningBalanceCommandEvaluator.EvaluateAsync(
                normalized,
                evaluatedAtUtc,
                _timeProvider.LocalTimeZone,
                _referenceStore,
                _historyStore,
                cancellationToken);

        return validated.ToPreview();
    }
}
