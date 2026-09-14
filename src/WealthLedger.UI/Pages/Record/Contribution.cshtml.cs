using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

public sealed class ContributionForm
{
    public string? PortfolioId { get; set; }

    public string? AccountId { get; set; }

    public string? CashAssetId { get; set; }

    public string? HouseholdMemberId { get; set; }

    public string? CategoryCode { get; set; }

    public string? Amount { get; set; }

    public string? ExecutionDate { get; set; }

    public string? ExternalReference { get; set; }

    public string? Note { get; set; }

    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Records external capital entering the ledger.
/// </summary>
/// <remarks>
/// A contribution is not a purchase. It is money arriving from outside the
/// household's recorded holdings, so it has one positive entry and no
/// consideration leg. M008 exposes the existing writer through a reviewed
/// workflow; it does not change how a contribution is accounted for.
/// </remarks>
[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class ContributionModel : PageModel
{
    private const int ChoicePageSize = 100;

    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ListFundTradeChoicesUseCase _listChoices;
    private readonly ListHouseholdMembersUseCase _listMembers;
    private readonly RecordContributionUseCase _record;
    private readonly ValuePresenter _values;
    private readonly UiText _text;
    private readonly TimeProvider _timeProvider;
    private readonly PresentationCulture _presentationCulture;

    public ContributionModel(
        ReadyHouseholdResolver householdResolver,
        ListFundTradeChoicesUseCase listChoices,
        ListHouseholdMembersUseCase listMembers,
        RecordContributionUseCase record,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _listChoices = listChoices
            ?? throw new ArgumentNullException(nameof(listChoices));
        _listMembers = listMembers
            ?? throw new ArgumentNullException(nameof(listMembers));
        _record = record
            ?? throw new ArgumentNullException(nameof(record));
        _values = values
            ?? throw new ArgumentNullException(nameof(values));
        _text = text
            ?? throw new ArgumentNullException(nameof(text));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    [BindProperty]
    public ContributionForm Input { get; set; } = new();

    public FundTradeChoices? Choices { get; private set; }

    public NavigationPage<HouseholdMemberNavigationItem>? Members
    { get; private set; }

    public Guid HouseholdId { get; private set; }

    public bool IsReview { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } =
        string.Empty;

    public Money? ReviewAmount { get; private set; }

    public string ReviewCurrencyCode { get; private set; } = string.Empty;

    public UiText Text => _text;

    public IReadOnlyList<FundTradeSelectOption> Categories { get; } =
    [
        new("SALARY", "Contribution_Category_Salary"),
        new("BONUS", "Contribution_Category_Bonus"),
        new("ACADEMIC_INCOME", "Contribution_Category_AcademicIncome"),
        new("GIFT", "Contribution_Category_Gift"),
        new("OTHER", "Contribution_Category_Other")
    ];

    public DisplayValue PresentMoney(long minorUnits, string currencyCode)
    {
        var currency =
            Choices?.Currencies.Items.SingleOrDefault(
                item => string.Equals(
                    item.Code,
                    currencyCode,
                    StringComparison.Ordinal));

        return _values.Money(
            minorUnits,
            currencyCode,
            currency?.MinorUnitDigits);
    }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        Input.PortfolioId ??= SingleOrNull(
            Choices!.Portfolios.Items.Select(x => x.PortfolioId));

        Input.AccountId ??= SingleOrNull(
            Choices!.CashAccounts.Items.Select(x => x.AccountId));

        Input.CashAssetId ??= SingleOrNull(
            Choices!.CashAssets.Items.Select(x => x.AssetId));

        Input.CategoryCode ??= "OTHER";
        Input.ExecutionDate ??= CurrentLocalDate().ToString("yyyy-MM-dd");
        Input.IdempotencyKey ??= Guid.NewGuid().ToString("D");

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        if (TryMap(out _))
        {
            IsReview = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPostAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        if (!FundTradeFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out var idempotencyKey))
        {
            AddFormError("Fund_Error_InvalidInput", 422);

            return Page();
        }

        if (!TryMap(out var command))
        {
            return Page();
        }

        try
        {
            var result = await _record.ExecuteAsync(
                idempotencyKey,
                command!,
                cancellationToken);

            /*
             * A contribution's receipt is the existing transaction detail.
             * M008 deliberately adds no second read model for it.
             */
            return RedirectToPage(
                "/Ledger/Details",
                new
                {
                    transactionId = result.TransactionId.ToString("D")
                });
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

        IsReview = true;

        return Page();
    }

    private bool TryMap(out RecordContributionCommand? command)
    {
        command = null;

        var portfolioId = ParseSelection(
            Input.PortfolioId,
            nameof(Input.PortfolioId),
            Choices!.Portfolios.Items.Select(x => x.PortfolioId));

        var accountId = ParseSelection(
            Input.AccountId,
            nameof(Input.AccountId),
            Choices.CashAccounts.Items.Select(x => x.AccountId));

        var cashAssetId = ParseSelection(
            Input.CashAssetId,
            nameof(Input.CashAssetId),
            Choices.CashAssets.Items.Select(x => x.AssetId));

        Guid? memberId = null;

        if (!string.IsNullOrWhiteSpace(Input.HouseholdMemberId))
        {
            memberId = ParseSelection(
                Input.HouseholdMemberId,
                nameof(Input.HouseholdMemberId),
                Members?.Items.Select(x => x.HouseholdMemberId) ?? []);
        }

        var asset =
            cashAssetId is null
                ? null
                : Choices.CashAssets.Items.SingleOrDefault(
                    x => x.AssetId == cashAssetId.Value);

        var currency =
            asset?.BaseCurrencyCode is null
                ? null
                : Choices.Currencies.Items.SingleOrDefault(
                    x => x.Code == asset.BaseCurrencyCode);

        var amount = ParseAmount(currency);
        var executionDate = ParseDate();
        var category = ParseCategory();

        if (portfolioId is null
            || accountId is null
            || cashAssetId is null
            || amount is null
            || executionDate is null
            || category is null
            || !ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

            return false;
        }

        ReviewAmount = amount;
        ReviewCurrencyCode = currency!.Code;

        command = new RecordContributionCommand(
            HouseholdId,
            portfolioId.Value,
            accountId.Value,
            cashAssetId.Value,
            amount,
            category.Value,
            executionDate.Value,
            memberId,
            Input.ExternalReference,
            Input.Note);

        return true;
    }

    private CashFlowCategory? ParseCategory()
        => Input.CategoryCode switch
        {
            "SALARY" => CashFlowCategory.Salary,
            "BONUS" => CashFlowCategory.Bonus,
            "ACADEMIC_INCOME" => CashFlowCategory.AcademicIncome,
            "GIFT" => CashFlowCategory.Gift,
            "OTHER" => CashFlowCategory.Other,
            _ => Invalid<CashFlowCategory?>(
                nameof(Input.CategoryCode),
                "Fund_Validation_ChoiceUnavailable")
        };

    private Money? ParseAmount(CurrencyNavigationItem? currency)
    {
        if (currency is null)
        {
            return null;
        }

        var normalized = Input.Amount?.Trim().Replace(',', '.');

        if (string.IsNullOrWhiteSpace(normalized)
            || !decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed <= 0)
        {
            return Invalid<Money?>(
                nameof(Input.Amount),
                "Fund_Validation_Amount");
        }

        try
        {
            long multiplier = 1;

            for (var digit = 0;
                 digit < currency.MinorUnitDigits;
                 digit++)
            {
                multiplier = checked(multiplier * 10);
            }

            var scaled = checked(parsed * multiplier);

            if (decimal.Truncate(scaled) != scaled)
            {
                return Invalid<Money?>(
                    nameof(Input.Amount),
                    "Fund_Validation_Amount");
            }

            return Money.FromMinorUnits(
                checked((long)scaled),
                new CurrencyCode(currency.Code));
        }
        catch (Exception exception)
            when (exception is ArgumentException or OverflowException)
        {
            return Invalid<Money?>(
                nameof(Input.Amount),
                "Fund_Validation_Amount");
        }
    }

    private DateOnly? ParseDate()
    {
        if (!DateOnly.TryParse(
                Input.ExecutionDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return Invalid<DateOnly?>(
                nameof(Input.ExecutionDate),
                "Fund_Validation_Date");
        }

        if (parsed > CurrentLocalDate())
        {
            return Invalid<DateOnly?>(
                nameof(Input.ExecutionDate),
                "Fund_Error_Date");
        }

        return parsed;
    }

    private Guid? ParseSelection(
        string? value,
        string fieldName,
        IEnumerable<Guid> allowed)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || !allowed.Contains(parsed))
        {
            return Invalid<Guid?>(
                fieldName,
                "Fund_Validation_ChoiceUnavailable");
        }

        return parsed;
    }

    private T Invalid<T>(string fieldName, string resourceKey)
    {
        ModelState.AddModelError(
            $"Input.{fieldName}",
            _text[resourceKey]);

        return default!;
    }

    private void AddFormError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, _text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    private DateOnly CurrentLocalDate()
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    _timeProvider.GetUtcNow(),
                    _presentationCulture.DisplayTimeZone)
                .DateTime);

    private async Task<bool> LoadChoicesAsync(
        CancellationToken cancellationToken)
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

                return false;
            }

            HouseholdId = resolution.Household!.HouseholdId;

            Choices = await _listChoices.ExecuteAsync(
                new ListFundTradeChoicesQuery(
                    HouseholdId,
                    PageSize: ChoicePageSize),
                cancellationToken);

            Members = await _listMembers.ExecuteAsync(
                new ListHouseholdMembersQuery(
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

    private static string? SingleOrNull(IEnumerable<Guid> candidates)
    {
        var only = candidates.Take(2).ToArray();

        return only.Length == 1
            ? only[0].ToString("D")
            : null;
    }
}
