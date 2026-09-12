using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class ReceiptModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetOpeningBalanceVerificationUseCase _getVerification;
    private readonly GetLedgerTransactionExplanationUseCase _getExplanation;
    private readonly ValuePresenter _values;
    private readonly PresentationCulture _presentationCulture;

    public ReceiptModel(
        ReadyHouseholdResolver householdResolver,
        GetOpeningBalanceVerificationUseCase getVerification,
        GetLedgerTransactionExplanationUseCase getExplanation,
        ValuePresenter values,
        PresentationCulture presentationCulture)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getVerification = getVerification
            ?? throw new ArgumentNullException(nameof(getVerification));
        _getExplanation = getExplanation
            ?? throw new ArgumentNullException(nameof(getExplanation));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    public bool InvalidIdentifier { get; private set; }

    public bool ReceiptNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public OpeningBalanceVerification? Verification { get; private set; }

    public LedgerTransactionExplanation? Explanation { get; private set; }

    public LedgerTransactionEntryCurrentContext? EntryContext
        => Explanation?.CurrentContext.Entries.SingleOrDefault();

    public LedgerTransactionEntryDetail? Entry
        => Verification?.Transaction.Entries.SingleOrDefault();

    public bool IsReversed
        => Verification?.Transaction.ReversedByTransactionId is not null;

    public decimal? TotalFineWeightGrams
    {
        get
        {
            var details = Verification?.Transaction.CreatedLots
                .Select(item => item.PhysicalGoldDetail)
                .Where(item => item is not null)
                .Select(item => item!.FineWeightGrams)
                .ToArray();

            if (details is null || details.Length == 0)
            {
                return null;
            }

            decimal total = 0;

            foreach (var value in details)
            {
                total = checked(total + value);
            }

            return total;
        }
    }

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(transactionId, "D", out var parsedId)
            || parsedId == Guid.Empty)
        {
            InvalidIdentifier = true;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

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
                return Page();
            }

            var householdId = resolution.Household!.HouseholdId;
            Verification = await _getVerification.ExecuteAsync(
                new GetOpeningBalanceVerificationQuery(
                    householdId,
                    parsedId),
                cancellationToken);
            Explanation = await _getExplanation.ExecuteAsync(
                new GetLedgerTransactionExplanationQuery(
                    householdId,
                    parsedId),
                cancellationToken);

            if (Explanation is null)
            {
                throw new NavigationPersistenceException(
                    "Opening-balance receipt context is incomplete.");
            }
        }
        catch (OpeningBalanceException exception)
            when (exception.Category == OpeningBalanceErrorCategory.NotFound)
        {
            ReceiptNotFound = true;
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

        return Page();
    }

    public DisplayValue PresentQuantity(long rawE8)
        => _values.Quantity(
            rawE8,
            EntryContext?.Asset.BaseUnit ?? AssetUnit.Other,
            QuantitySign.Absolute);

    public DisplayValue PresentMoney(long minorUnits, string currencyCode)
    {
        var currency = Explanation?.CurrentContext.Currencies.SingleOrDefault(
            item => string.Equals(
                item.Code,
                currencyCode,
                StringComparison.Ordinal));

        return _values.Money(
            minorUnits,
            currencyCode,
            currency?.MinorUnitDigits);
    }

    public DisplayValue PresentDate(DateOnly date)
        => _values.BusinessDate(date);

    public DisplayValue PresentTimestamp(DateTimeOffset timestamp)
        => _values.UtcTimestamp(timestamp);

    public string PresentExactDecimal(decimal value)
        => ExactDecimalText.Format(value, _presentationCulture.Culture);

    public string PresentFineness(int partsPerMillion)
        => ExactDecimalText.Format(
               partsPerMillion / 1_000m,
               _presentationCulture.Culture)
           + " ‰";

    public LedgerTransactionLotAllocationDetail AllocationFor(
        Guid assetLotId)
        => Verification!.Transaction.LotAllocations.Single(
            item => item.AssetLotId == assetLotId);
}
