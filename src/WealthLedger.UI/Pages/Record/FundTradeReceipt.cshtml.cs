using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// The persisted receipt of a fund trade.
/// </summary>
/// <remarks>
/// Everything shown is rebuilt from persisted facts, so the page survives a
/// restart and a refresh creates nothing. It is reachable by direct
/// navigation, which is exactly why it must never depend on anything carried
/// over from the posting request.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class FundTradeReceiptModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetFundTradeVerificationUseCase _getVerification;

    public FundTradeReceiptModel(
        ReadyHouseholdResolver householdResolver,
        GetFundTradeVerificationUseCase getVerification,
        ValuePresenter values,
        UiText text)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getVerification = getVerification
            ?? throw new ArgumentNullException(nameof(getVerification));
        Values = values
            ?? throw new ArgumentNullException(nameof(values));
        Text = text
            ?? throw new ArgumentNullException(nameof(text));
    }

    public ValuePresenter Values { get; }

    public UiText Text { get; }

    public bool InvalidIdentifier { get; private set; }

    public bool ReceiptNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } =
        string.Empty;

    public FundTradeVerification? Verification { get; private set; }

    public bool IsPurchase
        => Verification?.Facts.Type == TransactionType.Buy;

    public bool IsReversed
        => Verification?.Facts.ReversedByTransactionId is not null;

    public int MinorUnitDigits { get; private set; } = 2;

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(transactionId, "D", out var parsed))
        {
            InvalidIdentifier = true;
            Response.StatusCode = StatusCodes.Status400BadRequest;

            return Page();
        }

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

                return Page();
            }

            Verification = await _getVerification.ExecuteAsync(
                resolution.Household!.HouseholdId,
                parsed,
                cancellationToken);

            MinorUnitDigits = Verification.MinorUnitDigits;

            return Page();
        }
        catch (FundTradeException exception)
            when (exception.Category == FundTradeErrorCategory.NotFound)
        {
            ReceiptNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;

            return Page();
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or FundTradeException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;

            return Page();
        }
    }

}
