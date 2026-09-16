using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// Reconstructs a physical-gold receipt solely from persisted ledger facts.
/// </summary>
[LocalStartupPage(supportsPost: false, LocalStartupMode.Ready)]
public sealed class PhysicalGoldReceiptModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetPhysicalGoldActivityVerificationUseCase _verification;
    private readonly PresentationCulture _culture;

    public PhysicalGoldReceiptModel(
        ReadyHouseholdResolver householdResolver,
        GetPhysicalGoldActivityVerificationUseCase verification,
        ValuePresenter values,
        UiText text,
        PresentationCulture culture)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _verification = verification
            ?? throw new ArgumentNullException(nameof(verification));
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
    }

    public ValuePresenter Values { get; }

    public UiText Text { get; }

    public PhysicalGoldActivityVerification? Verification { get; private set; }

    public bool InvalidIdentifier { get; private set; }

    public bool ReceiptNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool IsPurchase => Verification?.Facts.Type == TransactionType.Buy;

    public bool IsSale => Verification?.Facts.Type == TransactionType.Sell;

    public bool IsTransfer => Verification?.Facts.Type == TransactionType.Transfer;

    public bool IsReversed
        => Verification?.Facts.ReversedByTransactionId is not null;

    public DisplayValue PresentGross(long rawE8, bool signed = false)
        => Values.Quantity(
            rawE8,
            Domain.Assets.AssetUnit.GrossGram,
            signed ? QuantitySign.SignedDelta : QuantitySign.Absolute);

    public DisplayValue PresentMoney(long minorUnits, string currencyCode)
        => Values.Money(
            minorUnits,
            currencyCode,
            Verification?.MinorUnitDigits);

    public string PresentFineWeight(decimal grams)
        => ExactDecimalText.Format(grams, _culture.Culture)
           + " "
           + Text["Gold_FineGramUnit"];

    public string PresentFineness(int ppm)
        => ExactDecimalText.Format((decimal)ppm / 1_000m, _culture.Culture)
           + " ‰ ("
           + ppm.ToString(_culture.Culture)
           + " ppm)";

    public string CostStatusCode(Domain.Lots.CostBasisStatus status)
        => FundTradeDisplayCodes.CostStatus(status);

    public string CompletenessCode(
        Domain.Lots.RealizedCostCompleteness completeness)
        => FundTradeDisplayCodes.Completeness(completeness);

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(transactionId, "D", out var parsed)
            || parsed == Guid.Empty)
        {
            InvalidIdentifier = true;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

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
                return Page();
            }

            Verification = await _verification.ExecuteAsync(
                resolution.Household!.HouseholdId,
                parsed,
                cancellationToken);
            return Page();
        }
        catch (PhysicalGoldException exception)
            when (exception.Category == PhysicalGoldErrorCategory.NotFound)
        {
            ReceiptNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or PhysicalGoldException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }
    }
}
