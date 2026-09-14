using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// The shared shape of a reviewed fund-trade workflow.
/// </summary>
/// <remarks>
/// Purchase and sale follow the same four stages: identify, enter source
/// facts, review the exact economic effect, then post and inspect the
/// persisted receipt. Only the middle two differ, so the staging, household
/// resolution, choice loading and error translation live here once.
///
/// Form state is never authoritative. It is re-posted on every step and
/// re-validated server-side, so nothing depends on session, cookie, TempData
/// or a stored draft.
/// </remarks>
public abstract class FundTradePageModel : PageModel
{
    private const int ChoicePageSize = 100;

    protected FundTradePageModel(
        ReadyHouseholdResolver householdResolver,
        ListFundTradeChoicesUseCase listChoices,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
    {
        HouseholdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        ListChoices = listChoices
            ?? throw new ArgumentNullException(nameof(listChoices));
        Values = values
            ?? throw new ArgumentNullException(nameof(values));
        Text = text
            ?? throw new ArgumentNullException(nameof(text));
        TimeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        PresentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    protected ReadyHouseholdResolver HouseholdResolver { get; }

    protected ListFundTradeChoicesUseCase ListChoices { get; }

    public ValuePresenter Values { get; }

    public UiText Text { get; }

    protected TimeProvider TimeProvider { get; }

    protected PresentationCulture PresentationCulture { get; }

    [BindProperty]
    public FundTradeForm Input { get; set; } = new();

    public FundTradeChoices? Choices { get; private set; }

    public Guid HouseholdId { get; private set; }

    public bool IsReview { get; protected set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } =
        string.Empty;

    /// <summary>
    /// The trade direction this page records.
    /// </summary>
    protected abstract TransactionType TradeType { get; }

    public bool ChoicesAreTruncated
        => Choices is not null
           && (Choices.Portfolios.NextCursor is not null
               || Choices.FundAccounts.NextCursor is not null
               || Choices.CashAccounts.NextCursor is not null
               || Choices.Currencies.NextCursor is not null
               || Choices.FundAssets.NextCursor is not null
               || Choices.CashAssets.NextCursor is not null);

    /// <summary>
    /// Whether the optional dates-and-costs section starts open.
    /// </summary>
    /// <remarks>
    /// Progressive disclosure must never hide a problem. The section opens
    /// whenever it already holds a value or carries a validation message, so
    /// an error is never collapsed out of sight.
    /// </remarks>
    public bool ShouldExpandOptionalDetails
        => !string.IsNullOrWhiteSpace(Input.OrderDate)
           || !string.IsNullOrWhiteSpace(Input.SettlementDate)
           || Input.Costs.Any(cost =>
               !string.IsNullOrWhiteSpace(cost.TypeCode)
               || !string.IsNullOrWhiteSpace(cost.Amount))
           || ModelState.Keys.Any(key =>
               ModelState[key]?.Errors.Count > 0
               && (key.Contains("Costs", StringComparison.Ordinal)
                   || key.Contains("OrderDate", StringComparison.Ordinal)
                   || key.Contains(
                       "SettlementDate",
                       StringComparison.Ordinal)));

    public IReadOnlyList<FundTradeSelectOption> CostTypes { get; } =
    [
        new("COMMISSION", "Fund_CostType_Commission"),
        new("BROKERAGE", "Fund_CostType_Brokerage"),
        new("WITHHOLDING_TAX", "Fund_CostType_WithholdingTax"),
        new("OTHER_TAX", "Fund_CostType_OtherTax"),
        new("OTHER", "Fund_CostType_Other")
    ];

    /// <summary>
    /// The treatments this direction allows.
    /// </summary>
    /// <remarks>
    /// A purchase has no proceeds, so withholding from them is not offered at
    /// all rather than offered and then rejected.
    /// </remarks>
    public IReadOnlyList<FundTradeSelectOption> CostTreatments
        => TradeType == TransactionType.Buy
            ?
            [
                new("ADDITIONAL_CASH_OUTFLOW",
                    "Fund_Treatment_AdditionalCashOutflow"),
                new("INCLUDED_IN_CONSIDERATION",
                    "Fund_Treatment_IncludedInConsideration"),
                new("INFORMATIONAL_ONLY",
                    "Fund_Treatment_InformationalOnly")
            ]
            :
            [
                new("ADDITIONAL_CASH_OUTFLOW",
                    "Fund_Treatment_AdditionalCashOutflow"),
                new("WITHHELD_FROM_PROCEEDS",
                    "Fund_Treatment_WithheldFromProceeds"),
                new("INCLUDED_IN_CONSIDERATION",
                    "Fund_Treatment_IncludedInConsideration"),
                new("INFORMATIONAL_ONLY",
                    "Fund_Treatment_InformationalOnly")
            ];

    /// <summary>
    /// Renders a quantity held in fund units.
    /// </summary>
    public DisplayValue PresentFundQuantity(
        long rawE8,
        bool signed = false)
        => Values.Quantity(
            rawE8,
            Domain.Assets.AssetUnit.FundUnit,
            signed ? QuantitySign.SignedDelta : QuantitySign.Absolute);

    /// <summary>
    /// Renders money using the precision the trade's currency actually has.
    /// </summary>
    /// <remarks>
    /// The digit count comes from the loaded currency rather than a default,
    /// so a nought-decimal or four-decimal currency is never shown with the
    /// wrong scale.
    /// </remarks>
    public DisplayValue PresentMoney(
        long minorUnits,
        string currencyCode)
    {
        var currency =
            Choices?.Currencies.Items.SingleOrDefault(
                item => string.Equals(
                    item.Code,
                    currencyCode,
                    StringComparison.Ordinal));

        return Values.Money(
            minorUnits,
            currencyCode,
            currency?.MinorUnitDigits);
    }

    public DisplayValue PresentUnitPrice(
        long rawE8,
        string currencyCode)
        => Values.UnitPrice(rawE8, currencyCode);

    public DisplayValue PresentDate(DateOnly date)
        => Values.BusinessDate(date);

    public string CostStatusCode(Domain.Lots.CostBasisStatus status)
        => FundTradeDisplayCodes.CostStatus(status);

    public string CompletenessCode(
        Domain.Lots.RealizedCostCompleteness completeness)
        => FundTradeDisplayCodes.Completeness(completeness);

    protected async Task<bool> LoadChoicesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var resolution =
                await HouseholdResolver.ResolveAsync(cancellationToken);

            if (resolution.State != ReadyHouseholdResolutionState.Single)
            {
                HouseholdStateResourceKey =
                    resolution.State == ReadyHouseholdResolutionState.None
                        ? "Ready_Household_None"
                        : "Ready_Household_Multiple";

                Response.StatusCode = StatusCodes.Status409Conflict;

                return false;
            }

            HouseholdId = resolution.Household!.HouseholdId;

            Choices = await ListChoices.ExecuteAsync(
                new ListFundTradeChoicesQuery(
                    HouseholdId,
                    PageSize: ChoicePageSize),
                cancellationToken);

            return true;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or FundTradeException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;

            return false;
        }
    }

    /// <summary>
    /// Prefills the parts of the form a user should not have to type.
    /// </summary>
    protected void InitializeForm()
    {
        if (Choices is null)
        {
            return;
        }

        Input.PortfolioId ??= SingleOrNull(
            Choices.Portfolios.Items.Select(x => x.PortfolioId));

        Input.FundAccountId ??= SingleOrNull(
            Choices.FundAccounts.Items.Select(x => x.AccountId));

        Input.CashAccountId ??= SingleOrNull(
            Choices.CashAccounts.Items.Select(x => x.AccountId));

        Input.FundAssetId ??= SingleOrNull(
            Choices.FundAssets.Items.Select(x => x.AssetId));

        Input.CashAssetId ??= SingleOrNull(
            Choices.CashAssets.Items.Select(x => x.AssetId));

        Input.ExecutionDate ??= CurrentLocalDate().ToString("yyyy-MM-dd");

        /*
         * The idempotency key identifies this attempt, not the trade. It is
         * generated here so a double submit of the same reviewed form is one
         * posting, while a deliberate second trade gets a new key.
         */
        Input.IdempotencyKey ??= Guid.NewGuid().ToString("D");

        if (Input.Costs.Count == 0)
        {
            Input.Costs.Add(new FundTradeCostForm());
        }
    }

    protected DateOnly CurrentLocalDate()
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    TimeProvider.GetUtcNow(),
                    PresentationCulture.DisplayTimeZone)
                .DateTime);

    /// <summary>
    /// Anchors an application failure to the field the user can act on.
    /// </summary>
    protected void AddFundTradeError(FundTradeException exception)
    {
        var field = exception.ErrorCode switch
        {
            FundTradeErrorCodes.QuantityInvalid => "Input.Quantity",
            FundTradeErrorCodes.PriceInvalid => "Input.UnitPrice",
            FundTradeErrorCodes.AmountInvalid
                or FundTradeErrorCodes.UnexplainedDiscrepancy
                or FundTradeErrorCodes.NegativeExpectedProceeds =>
                "Input.CashConsideration",
            FundTradeErrorCodes.DateInFuture
                or FundTradeErrorCodes.DateOrderInvalid
                or FundTradeErrorCodes.DateBeforeAccountOpening =>
                "Input.ExecutionDate",
            FundTradeErrorCodes.ProvenanceRequired
                or FundTradeErrorCodes.NegativeCashNoteRequired
                or FundTradeErrorCodes.SourceTextInvalid => "Input.Note",
            FundTradeErrorCodes.InsufficientFundQuantity => "Input.Quantity",
            _ => string.Empty
        };

        ModelState.AddModelError(
            field,
            Text[ResourceKeyFor(exception.ErrorCode)]);

        Response.StatusCode = exception.Category switch
        {
            FundTradeErrorCategory.NotFound =>
                StatusCodes.Status404NotFound,
            FundTradeErrorCategory.Conflict =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
    }

    /// <summary>
    /// Maps a stable failure code to explanatory Turkish text.
    /// </summary>
    /// <remarks>
    /// The code is translated rather than the exception message, so no
    /// internal detail reaches the page.
    /// </remarks>
    private static string ResourceKeyFor(string errorCode)
        => errorCode switch
        {
            FundTradeErrorCodes.ProvenanceRequired =>
                "Fund_Error_ProvenanceRequired",
            FundTradeErrorCodes.UnexplainedDiscrepancy =>
                "Fund_Error_UnexplainedDiscrepancy",
            FundTradeErrorCodes.NegativeExpectedProceeds =>
                "Fund_Error_NegativeExpectedProceeds",
            FundTradeErrorCodes.NegativeCashNoteRequired =>
                "Fund_Error_NegativeCashNoteRequired",
            FundTradeErrorCodes.InsufficientFundQuantity =>
                "Fund_Error_InsufficientQuantity",
            FundTradeErrorCodes.StaleReviewedPlan =>
                "Fund_Error_StaleReviewedPlan",
            FundTradeErrorCodes.ReviewedPlanRequired =>
                "Fund_Error_StaleReviewedPlan",
            FundTradeErrorCodes.CostTypeNotSupported
                or FundTradeErrorCodes.CostTreatmentNotSupported
                or FundTradeErrorCodes.CostAmountInvalid
                or FundTradeErrorCodes.CostDuplicate
                or FundTradeErrorCodes.CostLimitExceeded =>
                "Fund_Error_Cost",
            FundTradeErrorCodes.CurrencyMismatch =>
                "Fund_Error_CurrencyMismatch",
            FundTradeErrorCodes.DateInFuture
                or FundTradeErrorCodes.DateOrderInvalid
                or FundTradeErrorCodes.DateBeforeAccountOpening =>
                "Fund_Error_Date",
            FundTradeErrorCodes.ReferenceNotFound
                or FundTradeErrorCodes.NotFound =>
                "Fund_Error_ReferenceNotFound",
            FundTradeErrorCodes.ReferenceInactive
                or FundTradeErrorCodes.ReferenceShapeInvalid
                or FundTradeErrorCodes.HouseholdMismatch =>
                "Fund_Error_Reference",
            FundTradeErrorCodes.PersistenceConflict
                or FundTradeErrorCodes.UnsupportedPersistedShape =>
                "Fund_Error_Persistence",
            _ => "Fund_Error_InvalidInput"
        };

    protected void AddFormError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, Text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    protected void ApplyMappingErrors(
        IReadOnlyList<FundTradeFormError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(
                $"Input.{error.FieldName}",
                Text[error.ResourceKey]);
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        }
    }

    /// <summary>
    /// Runs an application call, translating every expected failure into a
    /// field-anchored message rather than an unhandled error.
    /// </summary>
    protected async Task<bool> TryExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();

            return true;
        }
        catch (FundTradeException exception)
        {
            AddFundTradeError(exception);
        }
        catch (IdempotencyConflictException)
        {
            AddFormError("Fund_Error_IdempotencyConflict", 409);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or ApplicationRuleViolationException
                  or DomainRuleViolationException)
        {
            AddFormError("Fund_Error_InvalidInput", 422);
        }
        catch (Exception exception)
            when (exception is NavigationPersistenceException
                  or CoreLedgerPersistenceException)
        {
            AddFormError("Fund_Error_Persistence", 409);
        }

        return false;
    }

    private static string? SingleOrNull(IEnumerable<Guid> candidates)
    {
        var only = candidates.Take(2).ToArray();

        return only.Length == 1
            ? only[0].ToString("D")
            : null;
    }
}

public sealed record FundTradeSelectOption(
    string Value,
    string ResourceKey);
