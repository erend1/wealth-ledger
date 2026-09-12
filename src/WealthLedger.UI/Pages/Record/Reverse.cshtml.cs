using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

public sealed class OpeningBalanceReversalForm
{
    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }
}

[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class ReverseModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetOpeningBalanceVerificationUseCase _getVerification;
    private readonly GetLedgerTransactionExplanationUseCase _getExplanation;
    private readonly PreviewPostedTransactionReversalUseCase _previewReversal;
    private readonly ReversePostedTransactionUseCase _reverseTransaction;
    private readonly ValuePresenter _values;
    private readonly UiText _text;

    public ReverseModel(
        ReadyHouseholdResolver householdResolver,
        GetOpeningBalanceVerificationUseCase getVerification,
        GetLedgerTransactionExplanationUseCase getExplanation,
        PreviewPostedTransactionReversalUseCase previewReversal,
        ReversePostedTransactionUseCase reverseTransaction,
        ValuePresenter values,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getVerification = getVerification
            ?? throw new ArgumentNullException(nameof(getVerification));
        _getExplanation = getExplanation
            ?? throw new ArgumentNullException(nameof(getExplanation));
        _previewReversal = previewReversal
            ?? throw new ArgumentNullException(nameof(previewReversal));
        _reverseTransaction = reverseTransaction
            ?? throw new ArgumentNullException(nameof(reverseTransaction));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    [BindProperty]
    public OpeningBalanceReversalForm Input { get; set; } = new();

    public bool InvalidIdentifier { get; private set; }

    public bool OpeningNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public OpeningBalanceVerification? Verification { get; private set; }

    public LedgerTransactionExplanation? Explanation { get; private set; }

    public ReversalPreviewResult? Preview { get; private set; }

    public Guid TransactionId { get; private set; }

    public AssetUnit AssetUnit
        => Explanation?.CurrentContext.Entries.SingleOrDefault()?.Asset.BaseUnit
           ?? AssetUnit.Other;

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!TryParseTransactionId(transactionId))
        {
            return Page();
        }

        Input.IdempotencyKey = Guid.NewGuid().ToString("D");
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!TryParseTransactionId(transactionId))
        {
            return Page();
        }

        await LoadAsync(cancellationToken);

        if (Verification is null || Preview is null)
        {
            return Page();
        }

        var reason = Input.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            ModelState.AddModelError(
                "Input.Reason",
                _text["OpeningReverse_Validation_ReasonRequired"]);
        }
        else if (reason.Length > 2_000 || reason.Any(char.IsControl))
        {
            ModelState.AddModelError(
                "Input.Reason",
                _text["OpeningReverse_Validation_ReasonInvalid"]);
        }

        if (!OpeningBalanceFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out var idempotencyKey))
        {
            ModelState.AddModelError(
                "Input.IdempotencyKey",
                _text["Opening_Validation_IdempotencyKey"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Preview.CanReverse)
        {
            AddOperationError(
                EligibilityResourceKey(Preview.EligibilityCode),
                409);
            return Page();
        }

        try
        {
            var result = await _reverseTransaction.ExecuteAsync(
                idempotencyKey,
                new ReversePostedTransactionCommand(
                    TransactionId,
                    reason!),
                cancellationToken);

            if (result is null)
            {
                OpeningNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }

            return RedirectToPage(
                "/Record/Receipt",
                new { transactionId = TransactionId.ToString("D") });
        }
        catch (ReversalCommandRejectedException exception)
        {
            AddOperationError(
                EligibilityResourceKey(exception.EligibilityCode),
                409);
            await ReloadPreviewAsync(cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            AddOperationError("Opening_Error_IdempotencyConflict", 409);
        }
        catch (ReversalReasonValidationException)
        {
            ModelState.AddModelError(
                "Input.Reason",
                _text["OpeningReverse_Validation_ReasonInvalid"]);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        }
        catch (CoreLedgerPersistenceException)
        {
            AddOperationError("OpeningReverse_Error_Persistence", 409);
        }

        return Page();
    }

    public DisplayValue PresentQuantity(long rawE8)
        => _values.Quantity(rawE8, AssetUnit, QuantitySign.SignedDelta);

    private bool TryParseTransactionId(string? transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "D", out var parsed)
            || parsed == Guid.Empty)
        {
            InvalidIdentifier = true;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        TransactionId = parsed;
        return true;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await _householdResolver.ResolveAsync(
                cancellationToken);

            if (resolution.State != ReadyHouseholdResolutionState.Single)
            {
                HouseholdStateResourceKey = resolution.State
                    == ReadyHouseholdResolutionState.None
                        ? "Ready_Household_None"
                        : "Ready_Household_Multiple";
                Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }

            var householdId = resolution.Household!.HouseholdId;
            Verification = await _getVerification.ExecuteAsync(
                new GetOpeningBalanceVerificationQuery(
                    householdId,
                    TransactionId),
                cancellationToken);
            Explanation = await _getExplanation.ExecuteAsync(
                new GetLedgerTransactionExplanationQuery(
                    householdId,
                    TransactionId),
                cancellationToken);
            Preview = await _previewReversal.ExecuteAsync(
                TransactionId,
                cancellationToken);

            if (Explanation is null || Preview is null)
            {
                throw new NavigationPersistenceException(
                    "Opening-balance reversal context is incomplete.");
            }
        }
        catch (OpeningBalanceException exception)
            when (exception.Category == OpeningBalanceErrorCategory.NotFound)
        {
            OpeningNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or CoreLedgerPersistenceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }
    }

    private async Task ReloadPreviewAsync(CancellationToken cancellationToken)
    {
        var current = await _previewReversal.ExecuteAsync(
            TransactionId,
            cancellationToken);

        if (current is not null)
        {
            Preview = current;
        }
    }

    private void AddOperationError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, _text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    public static string EligibilityResourceKey(ReversalEligibilityCode code)
        => code switch
        {
            ReversalEligibilityCode.Eligible =>
                "OpeningReverse_Eligible",
            ReversalEligibilityCode.AlreadyReversed =>
                "OpeningReverse_AlreadyReversed",
            ReversalEligibilityCode.BlockedByDependencies =>
                "OpeningReverse_BlockedByDependencies",
            ReversalEligibilityCode.NotPosted =>
                "OpeningReverse_NotPosted",
            ReversalEligibilityCode.TargetIsReversal =>
                "OpeningReverse_TargetIsReversal",
            ReversalEligibilityCode.UnsupportedPersistedShape =>
                "OpeningReverse_UnsupportedShape",
            _ => "OpeningReverse_UnsupportedShape"
        };
}
