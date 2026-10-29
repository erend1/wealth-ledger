using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// One evidence record: metadata, its retained file, and its links to
/// Posted transactions.
/// </summary>
/// <remarks>
/// The record itself is immutable. Links are separate insert-only
/// relationship records, and a mistaken record is corrected by superseding
/// it with a new one. The retained file is offered only as an explicit
/// download, never rendered in the page.
/// </remarks>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class EvidenceModel : ReconcilePageModel
{
    private const int CandidatePageSize = 20;

    private readonly GetEvidenceUseCase _getEvidence;
    private readonly GetEvidenceAttachmentContentUseCase _getAttachment;
    private readonly LinkEvidenceTransactionsUseCase _link;
    private readonly SearchLedgerTransactionsUseCase _search;
    private readonly EvidencePresenter _presenter;
    private readonly RecentLedgerPresenter _ledgerPresenter;

    public EvidenceModel(
        ReadyHouseholdResolver householdResolver,
        GetEvidenceUseCase getEvidence,
        GetEvidenceAttachmentContentUseCase getAttachment,
        LinkEvidenceTransactionsUseCase link,
        SearchLedgerTransactionsUseCase search,
        EvidencePresenter presenter,
        RecentLedgerPresenter ledgerPresenter,
        UiText text)
        : base(householdResolver, text)
    {
        _getEvidence = getEvidence
            ?? throw new ArgumentNullException(nameof(getEvidence));
        _getAttachment = getAttachment
            ?? throw new ArgumentNullException(nameof(getAttachment));
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _presenter = presenter
            ?? throw new ArgumentNullException(nameof(presenter));
        _ledgerPresenter = ledgerPresenter
            ?? throw new ArgumentNullException(nameof(ledgerPresenter));
    }

    public EvidenceDetailDisplay? Detail { get; private set; }

    public IReadOnlyList<RecentLedgerTransactionDisplay> Candidates { get; private set; } = [];

    public string? NextCandidateCursor { get; private set; }

    public bool CandidatesUnavailable { get; private set; }

    public int? LinkedCount { get; private set; }

    public int? AlreadyLinkedCount { get; private set; }

    public bool IsLinked(Guid transactionId)
        => Detail?.Links.Any(link => link.TransactionId == transactionId) ?? false;

    public async Task<IActionResult> OnGetAsync(
        Guid evidenceId,
        string? candidateCursor,
        int? linked,
        int? alreadyLinked,
        CancellationToken cancellationToken)
    {
        LinkedCount = linked is >= 0 ? linked : null;
        AlreadyLinkedCount = alreadyLinked is >= 0 ? alreadyLinked : null;
        await LoadAsync(evidenceId, candidateCursor, cancellationToken);
        return Page();
    }

    /// <summary>
    /// Serves the retained file as an attachment that the browser must not
    /// sniff, run, frame or cache. Its bytes were re-verified against the
    /// stored SHA-256 by the use case.
    /// </summary>
    public async Task<IActionResult> OnGetAttachmentAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return Page();
        }

        try
        {
            var content = await _getAttachment.ExecuteAsync(
                new GetEvidenceAttachmentContentQuery(HouseholdId, evidenceId),
                cancellationToken);

            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.CacheControl = "no-store";
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";

            return File(
                content.Content.ToArray(),
                EvidenceUploadMediaTypes.ToContentType(content.MediaType),
                content.FileName);
        }
        catch (EvidenceException exception)
        {
            await LoadAsync(evidenceId, candidateCursor: null, cancellationToken);
            AddFormError(exception);
        }
        catch (EvidencePersistenceException)
        {
            await LoadAsync(evidenceId, candidateCursor: null, cancellationToken);
            AddFormError("Evidence_Error_Persistence", StatusCodes.Status409Conflict);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLinkAsync(
        Guid evidenceId,
        string[]? transactionIds,
        CancellationToken cancellationToken)
    {
        var selected = new List<Guid>();

        foreach (var value in transactionIds ?? [])
        {
            if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
            {
                selected.Clear();
                break;
            }

            selected.Add(parsed);
        }

        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return Page();
        }

        if (selected.Count == 0)
        {
            await LoadAsync(evidenceId, candidateCursor: null, cancellationToken);
            AddFormError("Evidence_Validation_LinkSelection", StatusCodes.Status422UnprocessableEntity);
            return Page();
        }

        try
        {
            var result = await _link.ExecuteAsync(
                new LinkEvidenceTransactionsCommand(HouseholdId, evidenceId, selected),
                cancellationToken);

            return RedirectToPage(
                "/Reconcile/Evidence",
                new
                {
                    evidenceId = evidenceId.ToString("D"),
                    linked = result.AddedCount,
                    alreadyLinked = result.AlreadyLinkedCount
                });
        }
        catch (EvidenceException exception)
        {
            await LoadAsync(evidenceId, candidateCursor: null, cancellationToken);
            AddFormError(exception);
        }
        catch (Exception exception)
            when (exception is ArgumentException or EvidencePersistenceException)
        {
            await LoadAsync(evidenceId, candidateCursor: null, cancellationToken);
            AddFormError("Evidence_Error_Persistence", StatusCodes.Status409Conflict);
        }

        return Page();
    }

    private async Task LoadAsync(
        Guid evidenceId,
        string? candidateCursor,
        CancellationToken cancellationToken)
    {
        if (!HasHousehold || HouseholdId == Guid.Empty)
        {
            if (!await ResolveHouseholdAsync(cancellationToken))
            {
                return;
            }
        }

        try
        {
            var detail = await _getEvidence.ExecuteAsync(
                new GetEvidenceQuery(HouseholdId, evidenceId),
                cancellationToken);
            Detail = _presenter.PresentDetail(detail);
        }
        catch (EvidenceException exception)
            when (exception.Category == EvidenceErrorCategory.NotFound)
        {
            MarkNotFound("M010Error_EVIDENCE_NOT_FOUND");
            return;
        }
        catch (Exception exception)
            when (exception is EvidenceException or EvidencePersistenceException)
        {
            Unavailable();
            return;
        }

        if (Detail.IsSuperseded)
        {
            return;
        }

        await LoadCandidatesAsync(candidateCursor, cancellationToken);
    }

    /// <summary>
    /// Posted transactions a reviewer can link: those in the evidence's
    /// account, or else its institution, dated on or before its effective
    /// date. Search is the existing bounded M010 query, not a new one.
    /// </summary>
    private async Task LoadCandidatesAsync(
        string? candidateCursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _search.ExecuteAsync(
                new SearchLedgerTransactionsQuery(
                    HouseholdId,
                    ExecutedTo: Detail!.EffectiveDateValue,
                    AccountId: Detail.AccountId,
                    InstitutionId: Detail.AccountId is null ? Detail.InstitutionId : null,
                    PageSize: CandidatePageSize,
                    Cursor: string.IsNullOrWhiteSpace(candidateCursor) ? null : candidateCursor),
                cancellationToken);

            Candidates = page.Items.Select(_ledgerPresenter.Present).ToArray();
            NextCandidateCursor = page.NextCursor;
        }
        catch (Exception exception)
            when (exception is LedgerSearchRequestException
                  or LedgerSearchFilterScopeNotFoundException
                  or LedgerSearchHouseholdNotFoundException
                  or LedgerSearchPersistenceException)
        {
            CandidatesUnavailable = true;
        }
    }
}
