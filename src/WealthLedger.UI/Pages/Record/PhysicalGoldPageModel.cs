using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// Shared server-side state and validation for reviewed gold activity pages.
/// </summary>
public abstract class PhysicalGoldPageModel : PageModel
{
    private const int ChoicePageSize = 100;

    protected PhysicalGoldPageModel(
        ReadyHouseholdResolver householdResolver,
        ListPhysicalGoldChoicesUseCase listChoices,
        GetPhysicalGoldCustodyInventoryUseCase getCustody,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
    {
        HouseholdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        ListChoices = listChoices
            ?? throw new ArgumentNullException(nameof(listChoices));
        GetCustody = getCustody
            ?? throw new ArgumentNullException(nameof(getCustody));
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        TimeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        PresentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    protected ReadyHouseholdResolver HouseholdResolver { get; }

    protected ListPhysicalGoldChoicesUseCase ListChoices { get; }

    protected GetPhysicalGoldCustodyInventoryUseCase GetCustody { get; }

    protected TimeProvider TimeProvider { get; }

    protected PresentationCulture PresentationCulture { get; }

    public ValuePresenter Values { get; }

    public UiText Text { get; }

    [BindProperty]
    public PhysicalGoldForm Input { get; set; } = new();

    public PhysicalGoldChoices? Choices { get; private set; }

    public IReadOnlyList<PhysicalGoldCustodyPosition> Custody { get; private set; }
        = [];

    public Guid HouseholdId { get; private set; }

    public bool IsReview { get; protected set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    protected abstract PhysicalGoldWorkflow Workflow { get; }

    public bool IsPurchase => Workflow == PhysicalGoldWorkflow.Purchase;

    public bool IsSale => Workflow == PhysicalGoldWorkflow.Sale;

    public bool IsTransfer => Workflow == PhysicalGoldWorkflow.Transfer;

    public bool ChoicesAreTruncated
        => Choices is not null
           && (Choices.Portfolios.NextCursor is not null
               || Choices.GoldAccounts.NextCursor is not null
               || Choices.CashAccounts.NextCursor is not null
               || Choices.Counterparties.NextCursor is not null
               || Choices.Currencies.NextCursor is not null
               || Choices.GoldAssets.NextCursor is not null
               || Choices.CashAssets.NextCursor is not null);

    public bool ShouldExpandAdvanced
        => !string.IsNullOrWhiteSpace(Input.UnitPrice)
           || !string.IsNullOrWhiteSpace(Input.OrderDate)
           || !string.IsNullOrWhiteSpace(Input.SettlementDate)
           || !string.IsNullOrWhiteSpace(Input.Hallmark)
           || !string.IsNullOrWhiteSpace(Input.CertificateReference)
           || !string.IsNullOrWhiteSpace(Input.LotNote)
           || Input.Costs.Any(x =>
               !string.IsNullOrWhiteSpace(x.TypeCode)
               || !string.IsNullOrWhiteSpace(x.Amount))
           || ModelState.Keys.Any(key =>
               ModelState[key]?.Errors.Count > 0
               && (key.Contains("Costs", StringComparison.Ordinal)
                   || key.Contains("UnitPrice", StringComparison.Ordinal)
                   || key.Contains("OrderDate", StringComparison.Ordinal)
                   || key.Contains("SettlementDate", StringComparison.Ordinal)
                   || key.Contains("Hallmark", StringComparison.Ordinal)
                   || key.Contains("CertificateReference", StringComparison.Ordinal)
                   || key.Contains("LotNote", StringComparison.Ordinal)));

    public IReadOnlyList<PhysicalGoldOption> CostTypes { get; } =
    [
        new("MAKING_CHARGE", "Gold_CostType_MakingCharge"),
        new("COMMISSION", "Gold_CostType_Commission"),
        new("OTHER_TAX", "Gold_CostType_OtherTax"),
        new("INSURANCE", "Gold_CostType_Insurance"),
        new("OTHER", "Gold_CostType_Other")
    ];

    public IReadOnlyList<PhysicalGoldOption> CostTreatments
        => Workflow switch
        {
            PhysicalGoldWorkflow.Purchase =>
            [
                new("ADDITIONAL_CASH_OUTFLOW", "Gold_Treatment_Additional"),
                new("INCLUDED_IN_CONSIDERATION", "Gold_Treatment_Included"),
                new("INFORMATIONAL_ONLY", "Gold_Treatment_Informational")
            ],
            PhysicalGoldWorkflow.Sale =>
            [
                new("ADDITIONAL_CASH_OUTFLOW", "Gold_Treatment_Additional"),
                new("WITHHELD_FROM_PROCEEDS", "Gold_Treatment_Withheld"),
                new("INCLUDED_IN_CONSIDERATION", "Gold_Treatment_Included"),
                new("INFORMATIONAL_ONLY", "Gold_Treatment_Informational")
            ],
            _ =>
            [
                new("ADDITIONAL_CASH_OUTFLOW", "Gold_Treatment_Additional"),
                new("INFORMATIONAL_ONLY", "Gold_Treatment_Informational")
            ]
        };

    public IReadOnlyList<PhysicalGoldOption> FinenessChoices { get; } =
    [
        new("999.9", "Gold_Fineness_24K"),
        new("916", "Gold_Fineness_22K"),
        new("750", "Gold_Fineness_18K"),
        new("585", "Gold_Fineness_14K"),
        new("333", "Gold_Fineness_8K"),
        new("CUSTOM", "Gold_Fineness_Custom")
    ];

    public IReadOnlyList<PhysicalGoldCustodyPosition> AvailableLots
    {
        get
        {
            if (!TrySelectedSource(
                    out var portfolioId,
                    out var accountId,
                    out var assetId))
            {
                return [];
            }

            return Custody
                .Where(x => x.PortfolioId == portfolioId
                            && x.AccountId == accountId
                            && x.GoldAssetId == assetId
                            && x.GrossWeightRawE8 > 0
                            && x.PieceCount > 0)
                .OrderBy(x => x.AcquiredOn)
                .ThenBy(x => x.AssetLotId)
                .ToArray();
        }
    }

    public PhysicalGoldCustodyPosition? FindCustodyLot(string? lotId)
        => Guid.TryParseExact(lotId, "D", out var parsed)
            ? Custody.SingleOrDefault(x => x.AssetLotId == parsed)
            : null;

    public DisplayValue PresentGross(long rawE8, bool signed = false)
        => Values.Quantity(
            rawE8,
            Domain.Assets.AssetUnit.GrossGram,
            signed ? QuantitySign.SignedDelta : QuantitySign.Absolute);

    public DisplayValue PresentMoney(
        long minorUnits,
        string currencyCode,
        int? minorUnitDigits = null)
    {
        var digits = minorUnitDigits
            ?? Choices?.Currencies.Items.SingleOrDefault(
                x => x.Code == currencyCode)?.MinorUnitDigits;
        return Values.Money(minorUnits, currencyCode, digits);
    }

    public string PresentFineWeight(decimal grams)
        => ExactDecimalText.Format(grams, PresentationCulture.Culture)
           + " "
           + Text["Gold_FineGramUnit"];

    public string PresentFineness(int ppm)
        => ExactDecimalText.Format(
               (decimal)ppm / 1_000m,
               PresentationCulture.Culture)
           + " ‰ ("
           + ppm.ToString(PresentationCulture.Culture)
           + " ppm)";

    public string CostStatusCode(Domain.Lots.CostBasisStatus status)
        => FundTradeDisplayCodes.CostStatus(status);

    public string CompletenessCode(
        Domain.Lots.RealizedCostCompleteness completeness)
        => FundTradeDisplayCodes.Completeness(completeness);

    protected async Task<bool> LoadContextAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await HouseholdResolver.ResolveAsync(cancellationToken);
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
                new ListPhysicalGoldChoicesQuery(
                    HouseholdId,
                    PageSize: ChoicePageSize),
                cancellationToken);
            Custody = (await GetCustody.ExecuteAsync(
                HouseholdId,
                cancellationToken)).Items;
            return true;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or PhysicalGoldException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
            return false;
        }
    }

    protected void InitializeForm()
    {
        if (Choices is null)
        {
            return;
        }

        Input.PortfolioId ??= SingleOrNull(
            Choices.Portfolios.Items.Select(x => x.PortfolioId));
        Input.GoldAccountId ??= SingleOrNull(
            Choices.GoldAccounts.Items.Select(x => x.AccountId));
        Input.CashAccountId ??= SingleOrNull(
            Choices.CashAccounts.Items.Select(x => x.AccountId));
        Input.GoldAssetId ??= SingleOrNull(
            Choices.GoldAssets.Items.Select(x => x.AssetId));
        Input.CashAssetId ??= SingleOrNull(
            Choices.CashAssets.Items.Select(x => x.AssetId));
        Input.SourcePortfolioId ??= Input.PortfolioId;
        Input.DestinationPortfolioId ??= Input.PortfolioId;
        Input.CashPortfolioId ??= Input.PortfolioId;
        var goldAccounts = Choices.GoldAccounts.Items.Take(2).ToArray();
        if (goldAccounts.Length == 1)
        {
            Input.SourceGoldAccountId ??= goldAccounts[0].AccountId.ToString("D");
        }
        else if (goldAccounts.Length >= 2)
        {
            Input.SourceGoldAccountId ??= goldAccounts[0].AccountId.ToString("D");
            Input.DestinationGoldAccountId ??=
                goldAccounts[1].AccountId.ToString("D");
        }

        Input.ExecutionDate ??= CurrentLocalDate().ToString("yyyy-MM-dd");
        Input.FinenessChoice ??= "916";
        Input.IdempotencyKey ??= Guid.NewGuid().ToString("D");
        if (Input.Costs.Count == 0)
        {
            Input.Costs.Add(new PhysicalGoldCostForm());
        }

        SynchronizeSelectedLotRows(reset: false);
    }

    protected void SynchronizeSelectedLotRows(bool reset)
    {
        if (!reset && Input.SelectedLots.Count > 0)
        {
            return;
        }

        Input.SelectedLots = AvailableLots
            .Select(x => new PhysicalGoldSelectedLotForm
            {
                AssetLotId = x.AssetLotId.ToString("D")
            })
            .ToList();
        ModelState.Remove("Input.SelectedLots");
    }

    protected void ApplyMappingErrors(
        IReadOnlyList<PhysicalGoldFormError> errors)
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

    protected async Task<bool> TryExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (PhysicalGoldException exception)
        {
            AddPhysicalGoldError(exception);
        }
        catch (IdempotencyConflictException)
        {
            AddFormError("Gold_Error_IdempotencyConflict", 409);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or ApplicationRuleViolationException
                  or DomainRuleViolationException)
        {
            AddFormError("Gold_Error_InvalidInput", 422);
        }
        catch (Exception exception)
            when (exception is NavigationPersistenceException
                  or CoreLedgerPersistenceException)
        {
            AddFormError("Gold_Error_Persistence", 409);
        }

        return false;
    }

    protected void AddFormError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, Text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    private void AddPhysicalGoldError(PhysicalGoldException exception)
    {
        var field = exception.ErrorCode switch
        {
            PhysicalGoldErrorCodes.QuantityInvalid
                or PhysicalGoldErrorCodes.InsufficientGrossWeight =>
                IsPurchase ? "Input.GrossWeight" : "Input.SelectedLots",
            PhysicalGoldErrorCodes.PieceCountInvalid
                or PhysicalGoldErrorCodes.InsufficientPieces =>
                IsPurchase ? "Input.PieceCount" : "Input.SelectedLots",
            PhysicalGoldErrorCodes.FinenessInvalid => "Input.FinenessChoice",
            PhysicalGoldErrorCodes.PriceInvalid => "Input.UnitPrice",
            PhysicalGoldErrorCodes.AmountInvalid
                or PhysicalGoldErrorCodes.UnexplainedDiscrepancy
                or PhysicalGoldErrorCodes.NegativeExpectedProceeds =>
                "Input.CashConsideration",
            PhysicalGoldErrorCodes.DateInFuture
                or PhysicalGoldErrorCodes.DateOrderInvalid
                or PhysicalGoldErrorCodes.DateBeforeAccountOpening =>
                "Input.ExecutionDate",
            PhysicalGoldErrorCodes.ProvenanceRequired
                or PhysicalGoldErrorCodes.NegativeCashNoteRequired
                or PhysicalGoldErrorCodes.SourceTextInvalid => "Input.Note",
            PhysicalGoldErrorCodes.SelectedLotInvalid
                or PhysicalGoldErrorCodes.DuplicateSelection
                or PhysicalGoldErrorCodes.SelectedTotalsMismatch =>
                "Input.SelectedLots",
            _ => string.Empty
        };
        ModelState.AddModelError(field, Text[ResourceKeyFor(exception.ErrorCode)]);
        Response.StatusCode = exception.Category switch
        {
            PhysicalGoldErrorCategory.NotFound => StatusCodes.Status404NotFound,
            PhysicalGoldErrorCategory.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
    }

    private static string ResourceKeyFor(string errorCode)
        => errorCode switch
        {
            PhysicalGoldErrorCodes.ProvenanceRequired =>
                "Gold_Error_ProvenanceRequired",
            PhysicalGoldErrorCodes.UnexplainedDiscrepancy =>
                "Gold_Error_UnexplainedDiscrepancy",
            PhysicalGoldErrorCodes.NegativeExpectedProceeds =>
                "Gold_Error_NegativeExpectedProceeds",
            PhysicalGoldErrorCodes.NegativeCashNoteRequired =>
                "Gold_Error_NegativeCashNoteRequired",
            PhysicalGoldErrorCodes.InsufficientGrossWeight =>
                "Gold_Error_InsufficientGross",
            PhysicalGoldErrorCodes.InsufficientPieces =>
                "Gold_Error_InsufficientPieces",
            PhysicalGoldErrorCodes.StaleReviewedPlan
                or PhysicalGoldErrorCodes.ReviewedPlanRequired =>
                "Gold_Error_StalePlan",
            PhysicalGoldErrorCodes.CostTypeNotSupported
                or PhysicalGoldErrorCodes.CostTreatmentNotSupported
                or PhysicalGoldErrorCodes.CostAmountInvalid
                or PhysicalGoldErrorCodes.CostNoteRequired
                or PhysicalGoldErrorCodes.CostDuplicate
                or PhysicalGoldErrorCodes.CostLimitExceeded =>
                "Gold_Error_Cost",
            PhysicalGoldErrorCodes.CurrencyMismatch =>
                "Gold_Error_CurrencyMismatch",
            PhysicalGoldErrorCodes.DateInFuture
                or PhysicalGoldErrorCodes.DateOrderInvalid
                or PhysicalGoldErrorCodes.DateBeforeAccountOpening =>
                "Gold_Error_Date",
            PhysicalGoldErrorCodes.ReferenceNotFound
                or PhysicalGoldErrorCodes.NotFound =>
                "Gold_Error_ReferenceNotFound",
            PhysicalGoldErrorCodes.ReferenceInactive
                or PhysicalGoldErrorCodes.ReferenceShapeInvalid
                or PhysicalGoldErrorCodes.HouseholdMismatch
                or PhysicalGoldErrorCodes.CounterpartyInvalid
                or PhysicalGoldErrorCodes.TransferScopeInvalid
                or PhysicalGoldErrorCodes.CashScopeRequired
                or PhysicalGoldErrorCodes.CashScopeForbidden =>
                "Gold_Error_Reference",
            PhysicalGoldErrorCodes.SelectedLotInvalid
                or PhysicalGoldErrorCodes.DuplicateSelection
                or PhysicalGoldErrorCodes.SelectedTotalsMismatch =>
                "Gold_Error_SelectedLot",
            PhysicalGoldErrorCodes.PersistenceConflict
                or PhysicalGoldErrorCodes.UnsupportedPersistedShape =>
                "Gold_Error_Persistence",
            _ => "Gold_Error_InvalidInput"
        };

    private bool TrySelectedSource(
        out Guid portfolioId,
        out Guid accountId,
        out Guid assetId)
    {
        portfolioId = Guid.Empty;
        accountId = Guid.Empty;
        assetId = Guid.Empty;
        var portfolio = IsTransfer
            ? Input.SourcePortfolioId
            : Input.PortfolioId;
        var account = IsTransfer
            ? Input.SourceGoldAccountId
            : Input.GoldAccountId;
        return Guid.TryParseExact(portfolio, "D", out portfolioId)
               && Guid.TryParseExact(account, "D", out accountId)
               && Guid.TryParseExact(Input.GoldAssetId, "D", out assetId);
    }

    private DateOnly CurrentLocalDate()
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    TimeProvider.GetUtcNow(),
                    PresentationCulture.DisplayTimeZone)
                .DateTime);

    private static string? SingleOrNull(IEnumerable<Guid> candidates)
    {
        var values = candidates.Take(2).ToArray();
        return values.Length == 1 ? values[0].ToString("D") : null;
    }
}

public enum PhysicalGoldWorkflow
{
    Purchase,
    Sale,
    Transfer
}

public sealed record PhysicalGoldOption(string Value, string ResourceKey);
