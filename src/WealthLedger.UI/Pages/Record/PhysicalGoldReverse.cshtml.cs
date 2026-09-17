using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

public sealed class PhysicalGoldReversalForm
{
    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Posts an immutable exact reversal of an eligible physical-gold activity.
/// </summary>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(supportsPost: true, LocalStartupMode.Ready)]
public sealed class PhysicalGoldReverseModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetPhysicalGoldActivityVerificationUseCase _verification;
    private readonly PreviewPostedTransactionReversalUseCase _preview;
    private readonly ReversePostedTransactionUseCase _reverse;

    public PhysicalGoldReverseModel(
        ReadyHouseholdResolver householdResolver,
        GetPhysicalGoldActivityVerificationUseCase verification,
        PreviewPostedTransactionReversalUseCase preview,
        ReversePostedTransactionUseCase reverse,
        ValuePresenter values,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _verification = verification
            ?? throw new ArgumentNullException(nameof(verification));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _reverse = reverse ?? throw new ArgumentNullException(nameof(reverse));
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public ValuePresenter Values { get; }

    public UiText Text { get; }

    [BindProperty]
    public PhysicalGoldReversalForm Input { get; set; } = new();

    public Guid TransactionId { get; private set; }

    public PhysicalGoldActivityVerification? Verification { get; private set; }

    public ReversalPreviewResult? Preview { get; private set; }

    public bool InvalidIdentifier { get; private set; }

    public bool ActivityNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool IsPurchase => Verification?.Facts.Type == TransactionType.Buy;

    public bool IsSale => Verification?.Facts.Type == TransactionType.Sell;

    public DisplayValue PresentEntry(ReversalPreviewEntry entry)
        => Values.Quantity(
            entry.QuantityDelta.RawE8,
            entry.AssetId == Verification?.Facts.GoldAssetId
                ? Domain.Assets.AssetUnit.GrossGram
                : Domain.Assets.AssetUnit.CurrencyUnit,
            QuantitySign.SignedDelta);

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!TryParseTransactionId(transactionId))
        {
            return Page();
        }

        await LoadAsync(cancellationToken);
        Input.IdempotencyKey ??= Guid.NewGuid().ToString("D");
        return Page();
    }

    public async Task<IActionResult> OnPostReverseAsync(
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
                Text["GoldReverse_Validation_ReasonRequired"]);
        }
        else if (reason.Length > 2_000 || reason.Any(char.IsControl))
        {
            ModelState.AddModelError(
                "Input.Reason",
                Text["GoldReverse_Validation_ReasonInvalid"]);
        }

        if (!PhysicalGoldFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out var idempotencyKey))
        {
            ModelState.AddModelError(
                "Input.IdempotencyKey",
                Text["Gold_Error_InvalidInput"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Preview.CanReverse)
        {
            AddOperationError(EligibilityResourceKey(Preview.EligibilityCode), 409);
            return Page();
        }

        try
        {
            var result = await _reverse.ExecuteAsync(
                idempotencyKey,
                new ReversePostedTransactionCommand(TransactionId, reason!),
                cancellationToken);
            if (result is null)
            {
                ActivityNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }

            return RedirectToPage(
                "/Record/PhysicalGoldReceipt",
                new { transactionId = TransactionId.ToString("D") });
        }
        catch (ReversalCommandRejectedException exception)
        {
            AddOperationError(EligibilityResourceKey(exception.EligibilityCode), 409);
            await LoadAsync(cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            AddOperationError("Gold_Error_IdempotencyConflict", 409);
        }
        catch (ReversalReasonValidationException)
        {
            ModelState.AddModelError(
                "Input.Reason",
                Text["GoldReverse_Validation_ReasonInvalid"]);
        }
        catch (CoreLedgerPersistenceException)
        {
            AddOperationError("Gold_Error_Persistence", 409);
        }

        return Page();
    }

    public static string EligibilityResourceKey(ReversalEligibilityCode code)
        => code switch
        {
            ReversalEligibilityCode.Eligible => "GoldReverse_Eligible",
            ReversalEligibilityCode.AlreadyReversed =>
                "GoldReverse_AlreadyReversed",
            ReversalEligibilityCode.BlockedByDependencies =>
                "GoldReverse_BlockedByDependencies",
            ReversalEligibilityCode.TargetIsReversal =>
                "GoldReverse_TargetIsReversal",
            ReversalEligibilityCode.NotPosted => "GoldReverse_NotPosted",
            _ => "GoldReverse_UnsupportedShape"
        };

    private bool TryParseTransactionId(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
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
            var resolution = await _householdResolver.ResolveAsync(cancellationToken);
            if (resolution.State != ReadyHouseholdResolutionState.Single)
            {
                HouseholdStateResourceKey =
                    resolution.State == ReadyHouseholdResolutionState.None
                        ? "Ready_Household_None"
                        : "Ready_Household_Multiple";
                Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }

            Verification = await _verification.ExecuteAsync(
                resolution.Household!.HouseholdId,
                TransactionId,
                cancellationToken);
            Preview = await _preview.ExecuteAsync(TransactionId, cancellationToken);
            if (Preview is null)
            {
                ActivityNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
        catch (PhysicalGoldException exception)
            when (exception.Category == PhysicalGoldErrorCategory.NotFound)
        {
            ActivityNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or PhysicalGoldException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }
    }

    private void AddOperationError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, Text[resourceKey]);
        Response.StatusCode = statusCode;
    }
}
