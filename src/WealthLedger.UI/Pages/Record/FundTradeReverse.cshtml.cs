using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

public sealed class FundTradeReversalForm
{
    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Corrects a posted fund trade through an immutable reversal.
/// </summary>
/// <remarks>
/// The original trade is never edited or deleted. This page shows current
/// reversal eligibility, then posts a separate Posted reversal that mirrors
/// the original entries with opposite quantity deltas, restoring the lot
/// quantities the trade moved.
///
/// A corrected trade is a new, separately reviewed submission with its own
/// command identity. No durable replacement relationship is invented between
/// the two, because the ledger has no such fact to record.
/// </remarks>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class FundTradeReverseModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetFundTradeVerificationUseCase _getVerification;
    private readonly PreviewPostedTransactionReversalUseCase _previewReversal;
    private readonly ReversePostedTransactionUseCase _reverseTransaction;
    private readonly UiText _text;

    public FundTradeReverseModel(
        ReadyHouseholdResolver householdResolver,
        GetFundTradeVerificationUseCase getVerification,
        PreviewPostedTransactionReversalUseCase previewReversal,
        ReversePostedTransactionUseCase reverseTransaction,
        ValuePresenter values,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getVerification = getVerification
            ?? throw new ArgumentNullException(nameof(getVerification));
        _previewReversal = previewReversal
            ?? throw new ArgumentNullException(nameof(previewReversal));
        _reverseTransaction = reverseTransaction
            ?? throw new ArgumentNullException(nameof(reverseTransaction));
        Values = values
            ?? throw new ArgumentNullException(nameof(values));
        _text = text
            ?? throw new ArgumentNullException(nameof(text));
    }

    public ValuePresenter Values { get; }

    public UiText Text => _text;

    [BindProperty]
    public FundTradeReversalForm Input { get; set; } = new();

    public Guid TransactionId { get; private set; }

    public FundTradeVerification? Verification { get; private set; }

    public ReversalPreviewResult? Preview { get; private set; }

    public bool InvalidIdentifier { get; private set; }

    public bool TradeNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } =
        string.Empty;

    public bool IsPurchase
        => Verification?.Facts.Type == TransactionType.Buy;

    /// <summary>
    /// Renders a signed quantity effect in the asset's own unit.
    /// </summary>
    /// <remarks>
    /// The fund leg is measured in fund units and the cash legs in currency
    /// units, so the unit is chosen per entry rather than assumed.
    /// </remarks>
    public DisplayValue PresentEntry(ReversalPreviewEntry entry)
        => Values.Quantity(
            entry.QuantityDelta.RawE8,
            entry.AssetId == Verification?.Facts.Scope.FundAssetId
                ? Domain.Assets.AssetUnit.FundUnit
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
                _text["FundReverse_Validation_ReasonRequired"]);
        }
        else if (reason.Length > 2_000 || reason.Any(char.IsControl))
        {
            ModelState.AddModelError(
                "Input.Reason",
                _text["FundReverse_Validation_ReasonInvalid"]);
        }

        if (!FundTradeFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out var idempotencyKey))
        {
            ModelState.AddModelError(
                "Input.IdempotencyKey",
                _text["Fund_Error_InvalidInput"]);
        }

        /*
         * A field-level problem re-renders the form as it stands, matching
         * the opening-balance workflow. An application-level refusal below
         * still answers with its own status.
         */
        if (!ModelState.IsValid)
        {
            return Page();
        }

        /*
         * Eligibility is re-read on every post. A purchase whose units have
         * since been sold is blocked here rather than allowed to strand the
         * downstream sale.
         */
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
                TradeNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;

                return Page();
            }

            // Back to the original receipt, which now shows it was reversed.
            return RedirectToPage(
                "/Record/FundTradeReceipt",
                new { transactionId = TransactionId.ToString("D") });
        }
        catch (ReversalCommandRejectedException exception)
        {
            AddOperationError(
                EligibilityResourceKey(exception.EligibilityCode),
                409);

            await LoadAsync(cancellationToken);
        }
        catch (IdempotencyConflictException)
        {
            AddOperationError("Fund_Error_IdempotencyConflict", 409);
        }
        catch (ReversalReasonValidationException)
        {
            ModelState.AddModelError(
                "Input.Reason",
                _text["FundReverse_Validation_ReasonInvalid"]);
        }
        catch (CoreLedgerPersistenceException)
        {
            AddOperationError("Fund_Error_Persistence", 409);
        }

        return Page();
    }

    public static string EligibilityResourceKey(ReversalEligibilityCode code)
        => code switch
        {
            ReversalEligibilityCode.Eligible =>
                "FundReverse_Eligible",
            ReversalEligibilityCode.AlreadyReversed =>
                "FundReverse_AlreadyReversed",
            ReversalEligibilityCode.BlockedByDependencies =>
                "FundReverse_BlockedByDependencies",
            ReversalEligibilityCode.TargetIsReversal =>
                "FundReverse_TargetIsReversal",
            ReversalEligibilityCode.NotPosted =>
                "FundReverse_NotPosted",
            _ => "FundReverse_UnsupportedShape"
        };

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
            var resolution =
                await _householdResolver.ResolveAsync(cancellationToken);

            if (resolution.State != ReadyHouseholdResolutionState.Single)
            {
                HouseholdStateResourceKey =
                    resolution.State == ReadyHouseholdResolutionState.None
                        ? "Ready_Household_None"
                        : "Ready_Household_Multiple";

                Response.StatusCode = StatusCodes.Status409Conflict;

                return;
            }

            Verification = await _getVerification.ExecuteAsync(
                resolution.Household!.HouseholdId,
                TransactionId,
                cancellationToken);

            Preview = await _previewReversal.ExecuteAsync(
                TransactionId,
                cancellationToken);

            if (Preview is null)
            {
                TradeNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
        catch (FundTradeException exception)
            when (exception.Category == FundTradeErrorCategory.NotFound)
        {
            TradeNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or FundTradeException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }
    }

    private void AddOperationError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, _text[resourceKey]);
        Response.StatusCode = statusCode;
    }
}
