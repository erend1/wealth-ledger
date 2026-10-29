using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// Adds one exact observed quantity to an evidence record, after a review.
/// </summary>
/// <remarks>
/// An observation is insert-only. Recording the same scope and values again
/// is a no-op; a different value for a recorded scope is refused, and a
/// mistaken observation is corrected by superseding the evidence record.
/// </remarks>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class ObserveModel : ReconcilePageModel
{
    private readonly GetEvidenceUseCase _getEvidence;
    private readonly RecordEvidenceObservationsUseCase _record;
    private readonly ScopeChoiceLoader _choiceLoader;
    private readonly EvidencePresenter _evidencePresenter;
    private readonly ValuePresenter _values;

    public ObserveModel(
        ReadyHouseholdResolver householdResolver,
        GetEvidenceUseCase getEvidence,
        RecordEvidenceObservationsUseCase record,
        ScopeChoiceLoader choiceLoader,
        EvidencePresenter evidencePresenter,
        ValuePresenter values,
        UiText text)
        : base(householdResolver, text)
    {
        _getEvidence = getEvidence
            ?? throw new ArgumentNullException(nameof(getEvidence));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _choiceLoader = choiceLoader
            ?? throw new ArgumentNullException(nameof(choiceLoader));
        _evidencePresenter = evidencePresenter
            ?? throw new ArgumentNullException(nameof(evidencePresenter));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    [BindProperty]
    public ObservationForm Input { get; set; } = new();

    public EvidenceDetailDisplay? Evidence { get; private set; }

    public ScopeChoices? Choices { get; private set; }

    public bool IsReview { get; private set; }

    public ObservationReview? Review { get; private set; }

    public ScopeSelect Select(string field)
    {
        var choices = Choices ?? new ScopeChoices([], [], [], []);

        return field switch
        {
            nameof(ObservationForm.PortfolioId) => Create(
                field,
                "LedgerSearch_PortfolioLabel",
                ScopeChoiceLoader.PortfolioOptions(choices),
                Input.PortfolioId),
            nameof(ObservationForm.AccountId) => Create(
                field,
                "LedgerSearch_AccountLabel",
                ScopeChoiceLoader.AccountOptions(choices),
                Input.AccountId),
            nameof(ObservationForm.AssetId) => Create(
                field,
                "LedgerSearch_AssetLabel",
                ScopeChoiceLoader.AssetOptions(choices),
                Input.AssetId),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    public async Task<IActionResult> OnGetAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(evidenceId, cancellationToken))
        {
            return Page();
        }

        Input.PortfolioId ??= Choices!.Portfolios.Count == 1
            ? Choices.Portfolios[0].PortfolioId.ToString("D")
            : null;
        Input.AccountId ??= Evidence!.AccountId?.ToString("D");
        Input.Tolerance ??= "0";

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        if (await LoadAsync(evidenceId, cancellationToken)
            && TryMap(out _))
        {
            IsReview = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRecordAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(evidenceId, cancellationToken)
            || !TryMap(out var observation))
        {
            return Page();
        }

        try
        {
            var result = await _record.ExecuteAsync(
                new RecordEvidenceObservationsCommand(
                    HouseholdId,
                    evidenceId,
                    [observation!]),
                cancellationToken);

            return RedirectToPage(
                "/Reconcile/Evidence",
                new
                {
                    evidenceId = evidenceId.ToString("D"),
                    observed = result.AddedCount,
                    alreadyObserved = result.AlreadyRecordedCount
                });
        }
        catch (EvidenceException exception)
        {
            AddFormError(exception);
        }
        catch (Exception exception)
            when (exception is ArgumentException or EvidencePersistenceException)
        {
            AddFormError("Evidence_Error_Persistence", StatusCodes.Status409Conflict);
        }

        IsReview = true;
        return Page();
    }

    private bool TryMap(out EvidenceObservationInput? observation)
    {
        // Binding records entries under property names; the messages below
        // are keyed by the ids of the controls they name.
        ModelState.Clear();

        if (!ObservationFormMapper.TryMap(
                Input,
                Choices!,
                out observation,
                out var asset,
                out var errors))
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError("Input_" + error.Field, Text[error.ResourceKey]);
            }

            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return false;
        }

        var mapped = observation!;
        var portfolio = Choices!.Portfolios.Single(item => item.PortfolioId == mapped.PortfolioId);
        var account = Choices.Accounts.Single(item => item.AccountId == mapped.AccountId);

        Review = new ObservationReview(
            portfolio.Name,
            account.Name,
            account.Institution?.Name,
            asset!.Name,
            asset.Code,
            _values.Quantity(mapped.ObservedQuantityRawE8, asset.BaseUnit, QuantitySign.Absolute),
            _values.Quantity(mapped.ToleranceRawE8, asset.BaseUnit, QuantitySign.Absolute),
            mapped.ObservedPieceCount,
            mapped.ObservedQuantityRawE8,
            mapped.ToleranceRawE8);
        return true;
    }

    private async Task<bool> LoadAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            Evidence = _evidencePresenter.PresentDetail(
                await _getEvidence.ExecuteAsync(
                    new GetEvidenceQuery(HouseholdId, evidenceId),
                    cancellationToken));
            Choices = await _choiceLoader.LoadAsync(HouseholdId, cancellationToken);
        }
        catch (EvidenceException exception)
            when (exception.Category == EvidenceErrorCategory.NotFound)
        {
            MarkNotFound("M010Error_EVIDENCE_NOT_FOUND");
            return false;
        }
        catch (Exception exception)
            when (exception is EvidenceException
                  or EvidencePersistenceException
                  or NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            Unavailable();
            return false;
        }

        if (Evidence.IsSuperseded)
        {
            AddFormError("M010Error_EVIDENCE_SUPERSEDED", StatusCodes.Status409Conflict);
            return false;
        }

        return true;
    }

    private ScopeSelect Create(
        string field,
        string labelKey,
        IReadOnlyList<ScopeOption> options,
        string? selected)
        => new(
            "Input." + field,
            Text[labelKey],
            Text["Common_SelectPrompt"],
            Text["Scope_InactiveMarker"],
            options,
            selected,
            Id: "Input_" + field);
}

/// <summary>
/// The exact observation a reviewer confirms before it is recorded.
/// </summary>
public sealed record ObservationReview(
    string PortfolioName,
    string AccountName,
    string? InstitutionName,
    string AssetName,
    string AssetCode,
    DisplayValue Observed,
    DisplayValue Tolerance,
    int? Pieces,
    long ObservedRawE8,
    long ToleranceRawE8);
