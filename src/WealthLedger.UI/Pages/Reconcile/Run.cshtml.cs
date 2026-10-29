using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Reconciliation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// One stored reconciliation run, re-derived on every read so later ledger
/// history shows it as stale rather than silently rewriting it.
/// </summary>
/// <remarks>
/// <para>
/// Comparison and resolution stay separate. A documented exception stays
/// visibly out of tolerance, and a corrective transaction does not change the
/// stored delta: the page offers a rerun so a new comparison can match.
/// </para>
/// <para>
/// Nothing here posts a transaction. A correction is recorded through the
/// existing Record workflows and only linked here.
/// </para>
/// </remarks>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class RunModel : ReconcilePageModel
{
    private const int CorrectionCandidatePageSize = 10;

    private readonly GetReconciliationRunUseCase _getRun;
    private readonly GetEvidenceUseCase _getEvidence;
    private readonly RunReconciliationUseCase _run;
    private readonly ResolveReconciliationItemUseCase _resolve;
    private readonly SearchLedgerTransactionsUseCase _search;
    private readonly ReconciliationPresenter _presenter;
    private readonly EvidencePresenter _evidencePresenter;
    private readonly RecentLedgerPresenter _ledgerPresenter;

    public RunModel(
        ReadyHouseholdResolver householdResolver,
        GetReconciliationRunUseCase getRun,
        GetEvidenceUseCase getEvidence,
        RunReconciliationUseCase run,
        ResolveReconciliationItemUseCase resolve,
        SearchLedgerTransactionsUseCase search,
        ReconciliationPresenter presenter,
        EvidencePresenter evidencePresenter,
        RecentLedgerPresenter ledgerPresenter,
        UiText text)
        : base(householdResolver, text)
    {
        _getRun = getRun ?? throw new ArgumentNullException(nameof(getRun));
        _getEvidence = getEvidence
            ?? throw new ArgumentNullException(nameof(getEvidence));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
        _evidencePresenter = evidencePresenter
            ?? throw new ArgumentNullException(nameof(evidencePresenter));
        _ledgerPresenter = ledgerPresenter
            ?? throw new ArgumentNullException(nameof(ledgerPresenter));
    }

    public RunDetailDisplay? Run { get; private set; }

    public EvidenceDetailDisplay? Evidence { get; private set; }

    public bool Resolved { get; private set; }

    /// <summary>The item whose corrective-transaction choices are shown.</summary>
    public Guid? CorrectItemId { get; private set; }

    public IReadOnlyList<RecentLedgerTransactionDisplay> CorrectionCandidates
    {
        get;
        private set;
    } = [];

    public bool CorrectionCandidatesUnavailable { get; private set; }

    public string RerunKey { get; } = Guid.NewGuid().ToString("D");

    private (Guid ItemId, string Text)? _explanationDraft;

    /// <summary>
    /// The explanation a refused submission carried, so it is not lost.
    /// </summary>
    public string? ExplanationDraft(Guid itemId)
        => _explanationDraft is { } draft && draft.ItemId == itemId
            ? draft.Text
            : null;

    public bool CanRerun => Evidence is { IsSuperseded: false };

    public StatusBadge StaleBadge => _presenter.StaleBadge();

    public async Task<IActionResult> OnGetAsync(
        Guid runId,
        Guid? correct,
        bool? resolved,
        CancellationToken cancellationToken)
    {
        Resolved = resolved == true;
        await LoadAsync(runId, correct, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRerunAsync(
        Guid runId,
        string? rerunKey,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(runId, correct: null, cancellationToken))
        {
            return Page();
        }

        if (!EvidenceFormMapper.TryParseIdempotencyKey(rerunKey, out var key))
        {
            AddFormError("Evidence_Error_FormExpired", StatusCodes.Status422UnprocessableEntity);
            return Page();
        }

        try
        {
            var result = await _run.ExecuteAsync(
                key,
                new RunReconciliationCommand(HouseholdId, Run!.EvidenceId),
                cancellationToken);

            return RedirectToPage(
                "/Reconcile/Run",
                new { runId = result.ReconciliationRunId.ToString("D") });
        }
        catch (ReconciliationException exception)
        {
            AddFormError(exception);
        }
        catch (IdempotencyConflictException)
        {
            AddFormError("Evidence_Error_IdempotencyConflict", StatusCodes.Status409Conflict);
        }
        catch (Exception exception)
            when (exception is ArgumentException or ReconciliationPersistenceException)
        {
            AddFormError("Reconciliation_Error_Persistence", StatusCodes.Status409Conflict);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExceptionAsync(
        Guid runId,
        Guid itemId,
        string? explanation,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(runId, correct: null, cancellationToken))
        {
            return Page();
        }

        _explanationDraft = (itemId, explanation ?? string.Empty);

        if (string.IsNullOrWhiteSpace(explanation))
        {
            ModelState.AddModelError(
                ExplanationId(itemId),
                Text["Run_Validation_ExplanationRequired"]);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        return await ResolveAsync(
            runId,
            new ResolveReconciliationItemCommand(
                HouseholdId,
                runId,
                itemId,
                ReconciliationResolutionKind.DocumentedException,
                Explanation: explanation),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCorrectAsync(
        Guid runId,
        Guid itemId,
        string? correctiveTransactionId,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(runId, itemId, cancellationToken))
        {
            return Page();
        }

        if (!Guid.TryParseExact(correctiveTransactionId, "D", out var transactionId)
            || transactionId == Guid.Empty)
        {
            ModelState.AddModelError(
                CorrectionLegendId(itemId),
                Text["Run_Validation_CorrectionRequired"]);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        return await ResolveAsync(
            runId,
            new ResolveReconciliationItemCommand(
                HouseholdId,
                runId,
                itemId,
                ReconciliationResolutionKind.CorrectedByTransaction,
                CorrectiveTransactionId: transactionId),
            cancellationToken);
    }

    public static string ExplanationId(Guid itemId)
        => "explanation-" + itemId.ToString("N");

    public static string CorrectionLegendId(Guid itemId)
        => "correction-" + itemId.ToString("N");

    private async Task<IActionResult> ResolveAsync(
        Guid runId,
        ResolveReconciliationItemCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            await _resolve.ExecuteAsync(command, cancellationToken);

            return RedirectToPage(
                "/Reconcile/Run",
                new { runId = runId.ToString("D"), resolved = true });
        }
        catch (ReconciliationException exception)
        {
            AddFormError(exception);
        }
        catch (Exception exception)
            when (exception is ArgumentException or ReconciliationPersistenceException)
        {
            AddFormError("Reconciliation_Error_Persistence", StatusCodes.Status409Conflict);
        }

        return Page();
    }

    private async Task<bool> LoadAsync(
        Guid runId,
        Guid? correct,
        CancellationToken cancellationToken)
    {
        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            Run = _presenter.PresentDetail(
                await _getRun.ExecuteAsync(
                    new GetReconciliationRunQuery(HouseholdId, runId),
                    cancellationToken));
            Evidence = _evidencePresenter.PresentDetail(
                await _getEvidence.ExecuteAsync(
                    new GetEvidenceQuery(HouseholdId, Run.EvidenceId),
                    cancellationToken));
        }
        catch (ReconciliationException exception)
            when (exception.Category == ReconciliationErrorCategory.NotFound)
        {
            MarkNotFound("M010Error_RECONCILIATION_RUN_NOT_FOUND");
            return false;
        }
        catch (Exception exception)
            when (exception is ReconciliationException
                  or ReconciliationPersistenceException
                  or EvidenceException
                  or EvidencePersistenceException)
        {
            Unavailable();
            return false;
        }

        var item = correct is null
            ? null
            : Run.Items.SingleOrDefault(candidate => candidate.ItemId == correct && candidate.CanResolve);

        if (item is not null)
        {
            CorrectItemId = item.ItemId;
            await LoadCorrectionCandidatesAsync(item, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Recent Posted transactions in the item's exact scope, the usual place
    /// for a correction recorded through the existing Record workflows.
    /// </summary>
    private async Task LoadCorrectionCandidatesAsync(
        RunItemDisplay item,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _search.ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    AssetId: item.AssetId,
                    AccountId: item.AccountId,
                    PortfolioId: item.PortfolioId,
                    PageSize: CorrectionCandidatePageSize),
                cancellationToken);
            CorrectionCandidates = page.Items.Select(_ledgerPresenter.Present).ToArray();
        }
        catch (Exception exception)
            when (exception is LedgerSearchRequestException
                  or LedgerSearchFilterScopeNotFoundException
                  or LedgerSearchHouseholdNotFoundException
                  or LedgerSearchPersistenceException)
        {
            CorrectionCandidatesUnavailable = true;
        }
    }
}
