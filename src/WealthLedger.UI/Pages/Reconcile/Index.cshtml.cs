using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LocalData;
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
    private readonly EvidencePresenter _presenter;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        ListEvidenceUseCase listEvidence,
        EvidencePresenter presenter,
        UiText text)
        : base(householdResolver, text)
    {
        _listEvidence = listEvidence
            ?? throw new ArgumentNullException(nameof(listEvidence));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
    }

    public IReadOnlyList<EvidenceSummaryDisplay> Evidence { get; private set; } = [];

    public string? NextEvidenceCursor { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? evidenceCursor,
        CancellationToken cancellationToken)
    {
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
        }
        catch (EvidenceException exception) when (IsNavigationCode(exception.ErrorCode))
        {
            InvalidNavigation();
        }
        catch (Exception exception)
            when (exception is EvidenceException or EvidencePersistenceException)
        {
            Unavailable();
        }

        return Page();
    }
}
