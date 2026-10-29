using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Reconciliation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// Retained evidence for the Ready household.
/// </summary>
/// <remarks>
/// Evidence is adjunct audit data. Nothing on this page posts, edits or
/// deletes a ledger fact, and no list embeds retained file bytes.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : ReconcilePageModel
{
    private const int PageSize = 20;

    private readonly ListEvidenceUseCase _listEvidence;
    private readonly ListReconciliationRunsUseCase _listRuns;
    private readonly EvidencePresenter _presenter;
    private readonly ReconciliationPresenter _reconciliationPresenter;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        ListEvidenceUseCase listEvidence,
        ListReconciliationRunsUseCase listRuns,
        EvidencePresenter presenter,
        ReconciliationPresenter reconciliationPresenter,
        UiText text)
        : base(householdResolver, text)
    {
        _listEvidence = listEvidence
            ?? throw new ArgumentNullException(nameof(listEvidence));
        _listRuns = listRuns
            ?? throw new ArgumentNullException(nameof(listRuns));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
        _reconciliationPresenter = reconciliationPresenter
            ?? throw new ArgumentNullException(nameof(reconciliationPresenter));
    }

    public IReadOnlyList<EvidenceSummaryDisplay> Evidence { get; private set; } = [];

    public string? NextEvidenceCursor { get; private set; }

    public IReadOnlyList<RunSummaryDisplay> Runs { get; private set; } = [];

    public string? NextRunCursor { get; private set; }

    /// <summary>The cursors this page was opened with, so paging one list keeps the other.</summary>
    public string? EvidenceCursor { get; private set; }

    public string? RunCursor { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? evidenceCursor,
        string? runCursor,
        CancellationToken cancellationToken)
    {
        EvidenceCursor = evidenceCursor;
        RunCursor = runCursor;

        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return Page();
        }

        try
        {
            var page = await _listEvidence.ExecuteAsync(
                new ListEvidenceQuery(HouseholdId, PageSize, evidenceCursor),
                cancellationToken);
            Evidence = page.Items.Select(_presenter.PresentSummary).ToArray();
            NextEvidenceCursor = page.NextCursor;

            // The run list shows stored counts. Staleness needs the ledger
            // side re-derived, so only a run's own page reports it.
            var runs = await _listRuns.ExecuteAsync(
                new ListReconciliationRunsQuery(HouseholdId, PageSize: PageSize, Cursor: runCursor),
                cancellationToken);
            Runs = runs.Items.Select(_reconciliationPresenter.PresentSummary).ToArray();
            NextRunCursor = runs.NextCursor;
        }
        catch (EvidenceException exception) when (IsNavigationCode(exception.ErrorCode))
        {
            InvalidNavigation();
        }
        catch (ReconciliationException exception) when (IsNavigationCode(exception.ErrorCode))
        {
            InvalidNavigation();
        }
        catch (Exception exception)
            when (exception is EvidenceException
                  or EvidencePersistenceException
                  or ReconciliationException
                  or ReconciliationPersistenceException)
        {
            Unavailable();
        }

        return Page();
    }
}
