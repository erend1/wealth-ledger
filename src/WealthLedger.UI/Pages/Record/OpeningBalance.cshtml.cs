using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.Common;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class OpeningBalanceModel : PageModel
{
    private const int ChoicePageSize = 100;
    private const int MaximumLots = 50;

    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ListOpeningBalanceChoicesUseCase _listChoices;
    private readonly CreateOpeningBalanceCurrencyUseCase _createCurrency;
    private readonly CreateOpeningBalanceInstitutionUseCase _createInstitution;
    private readonly CreateOpeningBalanceAccountUseCase _createAccount;
    private readonly CreateOpeningBalanceAssetUseCase _createAsset;
    private readonly PreviewOpeningBalanceUseCase _previewOpeningBalance;
    private readonly RecordOpeningBalanceUseCase _recordOpeningBalance;
    private readonly ValuePresenter _values;
    private readonly UiText _text;
    private readonly TimeProvider _timeProvider;
    private readonly PresentationCulture _presentationCulture;

    public OpeningBalanceModel(
        ReadyHouseholdResolver householdResolver,
        ListOpeningBalanceChoicesUseCase listChoices,
        CreateOpeningBalanceCurrencyUseCase createCurrency,
        CreateOpeningBalanceInstitutionUseCase createInstitution,
        CreateOpeningBalanceAccountUseCase createAccount,
        CreateOpeningBalanceAssetUseCase createAsset,
        PreviewOpeningBalanceUseCase previewOpeningBalance,
        RecordOpeningBalanceUseCase recordOpeningBalance,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _listChoices = listChoices
            ?? throw new ArgumentNullException(nameof(listChoices));
        _createCurrency = createCurrency
            ?? throw new ArgumentNullException(nameof(createCurrency));
        _createInstitution = createInstitution
            ?? throw new ArgumentNullException(nameof(createInstitution));
        _createAccount = createAccount
            ?? throw new ArgumentNullException(nameof(createAccount));
        _createAsset = createAsset
            ?? throw new ArgumentNullException(nameof(createAsset));
        _previewOpeningBalance = previewOpeningBalance
            ?? throw new ArgumentNullException(nameof(previewOpeningBalance));
        _recordOpeningBalance = recordOpeningBalance
            ?? throw new ArgumentNullException(nameof(recordOpeningBalance));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));

        InstitutionTypes = Options(
            Enum.GetValues<InstitutionType>(),
            value => _values.StableCode(value));
        AccountTypes = Options(
            new[]
            {
                AccountType.Cash,
                AccountType.Investment,
                AccountType.PhysicalVault,
                AccountType.Pension
            },
            value => _values.StableCode(value));
        AssetTypes = Options(
            new[]
            {
                AssetType.Cash,
                AssetType.Currency,
                AssetType.Fund,
                AssetType.Equity,
                AssetType.PhysicalGold
            },
            value => _values.StableCode(value));
        LotTrackingModes = Options(
            Enum.GetValues<LotTrackingMode>(),
            value => _values.StableCode(value));
    }

    [BindProperty]
    public OpeningBalanceForm Input { get; set; } = new();

    [BindProperty]
    public OpeningBalanceCurrencyForm CurrencyInput { get; set; } = new();

    [BindProperty]
    public OpeningBalanceInstitutionForm InstitutionInput { get; set; } = new();

    [BindProperty]
    public OpeningBalanceAccountForm AccountInput { get; set; } = new();

    [BindProperty]
    public OpeningBalanceAssetForm AssetInput { get; set; } = new();

    public OpeningBalanceChoices? Choices { get; private set; }

    public OpeningBalancePreview? Preview { get; private set; }

    public bool IsReview { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public string NoticeResourceKey { get; private set; } = string.Empty;

    public Guid? RelatedTransactionId { get; private set; }

    public IReadOnlyList<OpeningBalanceSelectOption> InstitutionTypes { get; }

    public IReadOnlyList<OpeningBalanceSelectOption> AccountTypes { get; }

    public IReadOnlyList<OpeningBalanceSelectOption> AssetTypes { get; }

    public IReadOnlyList<OpeningBalanceSelectOption> LotTrackingModes { get; }

    public IReadOnlyList<FinenessChoice> FinenessChoices => FinenessChoice.All;

    public AssetNavigationItem? SelectedAsset
        => Choices is null
            || !Guid.TryParseExact(Input.AssetId, "D", out var assetId)
            ? null
            : Choices.Assets.Items.SingleOrDefault(
                item => item.AssetId == assetId);

    public bool SelectedAssetUsesLots
        => SelectedAsset?.Type is AssetType.Fund
            or AssetType.Equity
            or AssetType.PhysicalGold;

    public bool SelectedAssetIsGold
        => SelectedAsset?.Type == AssetType.PhysicalGold;

    public IReadOnlyList<AccountNavigationItem> AvailableAccounts
        => Choices is null
            ? []
            : SelectedAsset is null
                ? Choices.Accounts.Items
                : Choices.Accounts.Items
                    .Where(item => IsCompatible(item.Type, SelectedAsset.Type))
                    .ToArray();

    public bool ChoicesAreTruncated
        => Choices is not null
           && (Choices.Portfolios.NextCursor is not null
               || Choices.Accounts.NextCursor is not null
               || Choices.Institutions.NextCursor is not null
               || Choices.Currencies.NextCursor is not null
               || Choices.Assets.NextCursor is not null);

    public async Task<IActionResult> OnGetAsync(
        string? accountId,
        string? assetId,
        string? notice,
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        InitializeForm(accountId, assetId);
        NoticeResourceKey = notice switch
        {
            "currency-created" => "Opening_Notice_CurrencyCreated",
            "currency-equivalent" => "Opening_Notice_CurrencyEquivalent",
            "institution-created" => "Opening_Notice_InstitutionCreated",
            "institution-equivalent" => "Opening_Notice_InstitutionEquivalent",
            "account-created" => "Opening_Notice_AccountCreated",
            "account-equivalent" => "Opening_Notice_AccountEquivalent",
            "asset-created" => "Opening_Notice_AssetCreated",
            "asset-equivalent" => "Opening_Notice_AssetEquivalent",
            _ => string.Empty
        };

        return Page();
    }

    public async Task<IActionResult> OnPostSelectAssetAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        Input.Lots ??= [];
        Input.Quantity = null;
        ModelState.Remove("Input.Quantity");
        RemoveModelStatePrefix("Input.Lots");

        if (SelectedAsset is null)
        {
            ModelState.AddModelError(
                "Input.AssetId",
                _text["Opening_Validation_ChoiceUnavailable"]);
            Input.Lots.Clear();
        }
        else
        {
            Input.Lots = SelectedAssetUsesLots
                ? [new OpeningBalanceLotForm()]
                : [];

            if (!Guid.TryParseExact(Input.AccountId, "D", out var accountId)
                || !AvailableAccounts.Any(item => item.AccountId == accountId))
            {
                Input.AccountId = AvailableAccounts.Count == 1
                    ? AvailableAccounts[0].AccountId.ToString("D")
                    : null;
            }

            ModelState.Remove("Input.AccountId");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddLotAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        Input.Lots ??= [];

        if (!SelectedAssetUsesLots)
        {
            ModelState.AddModelError(
                "Input.Lots",
                _text["Opening_Validation_LotsForbidden"]);
        }
        else if (Input.Lots.Count >= MaximumLots)
        {
            ModelState.AddModelError(
                "Input.Lots",
                _text["Opening_Validation_TooManyLots"]);
        }
        else
        {
            Input.Lots.Add(new OpeningBalanceLotForm());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLotAsync(
        int lotIndex,
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        Input.Lots ??= [];

        if (lotIndex >= 0 && lotIndex < Input.Lots.Count)
        {
            Input.Lots.RemoveAt(lotIndex);
        }

        if (SelectedAssetUsesLots && Input.Lots.Count == 0)
        {
            Input.Lots.Add(new OpeningBalanceLotForm());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        var mapping = MapFinancialForm();

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            Preview = await _previewOpeningBalance.ExecuteAsync(
                mapping.Command!,
                cancellationToken);
            IsReview = true;
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or ApplicationRuleViolationException
                  or DomainRuleViolationException)
        {
            AddFormError("Opening_Error_InvalidInput", 422);
        }
        catch (Exception exception)
            when (exception is NavigationPersistenceException
                  or CoreLedgerPersistenceException)
        {
            AddFormError("Opening_Error_Persistence", 409);
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

        var mapping = MapFinancialForm();

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            var result = await _recordOpeningBalance.ExecuteAsync(
                Input.IdempotencyKey!.Trim(),
                mapping.Command!,
                cancellationToken);

            return RedirectToPage(
                "/Record/Receipt",
                new { transactionId = result.TransactionId.ToString("D") });
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (IdempotencyConflictException)
        {
            AddFormError("Opening_Error_IdempotencyConflict", 409);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or OverflowException
                  or ApplicationRuleViolationException
                  or DomainRuleViolationException)
        {
            AddFormError("Opening_Error_InvalidInput", 422);
        }
        catch (CoreLedgerPersistenceException)
        {
            AddFormError("Opening_Error_Persistence", 409);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateCurrencyAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        var mapping = OpeningBalanceReferenceFormMapper.MapCurrency(
            CurrencyInput);
        AddErrors(mapping.Errors, prefixFinancialInput: false);

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            var result = await _createCurrency.ExecuteAsync(
                mapping.Command!,
                cancellationToken);
            return RedirectToPage(
                new
                {
                    notice = result.WasCreated
                        ? "currency-created"
                        : "currency-equivalent"
                });
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (ArgumentException)
        {
            AddFormError("Opening_Error_ReferenceInvalid", 422);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateInstitutionAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        var mapping = OpeningBalanceReferenceFormMapper.MapInstitution(
            InstitutionInput);
        AddErrors(mapping.Errors, prefixFinancialInput: false);

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            var result = await _createInstitution.ExecuteAsync(
                mapping.Command!,
                cancellationToken);
            return RedirectToPage(
                new
                {
                    notice = result.WasCreated
                        ? "institution-created"
                        : "institution-equivalent"
                });
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (ArgumentException)
        {
            AddFormError("Opening_Error_ReferenceInvalid", 422);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAccountAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        var mapping = OpeningBalanceReferenceFormMapper.MapAccount(
            AccountInput,
            Choices!.Household.HouseholdId);
        AddErrors(mapping.Errors, prefixFinancialInput: false);

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            var result = await _createAccount.ExecuteAsync(
                mapping.Command!,
                cancellationToken);
            return RedirectToPage(
                new
                {
                    accountId = result.Reference.AccountId.ToString("D"),
                    notice = result.WasCreated
                        ? "account-created"
                        : "account-equivalent"
                });
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (ArgumentException)
        {
            AddFormError("Opening_Error_ReferenceInvalid", 422);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAssetAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        var mapping = OpeningBalanceReferenceFormMapper.MapAsset(AssetInput);
        AddErrors(mapping.Errors, prefixFinancialInput: false);

        if (!mapping.Succeeded)
        {
            return Page();
        }

        try
        {
            var result = await _createAsset.ExecuteAsync(
                mapping.Command!,
                cancellationToken);
            return RedirectToPage(
                new
                {
                    assetId = result.Reference.AssetId.ToString("D"),
                    notice = result.WasCreated
                        ? "asset-created"
                        : "asset-equivalent"
                });
        }
        catch (OpeningBalanceException exception)
        {
            AddOpeningBalanceError(exception);
        }
        catch (ArgumentException)
        {
            AddFormError("Opening_Error_ReferenceInvalid", 422);
        }

        return Page();
    }

    public DisplayValue PresentQuantity(long rawE8, AssetUnit unit)
        => _values.Quantity(rawE8, unit, QuantitySign.Absolute);

    public DisplayValue PresentAssetType(AssetType value)
        => _values.StableCode(value);

    public DisplayValue PresentAssetUnit(AssetUnit value)
        => _values.StableCode(value);

    public DisplayValue PresentLotTrackingMode(LotTrackingMode value)
        => _values.StableCode(value);

    public DisplayValue PresentMoney(long minorUnits, string currencyCode)
    {
        var currency = Choices?.Currencies.Items.SingleOrDefault(
            item => string.Equals(
                item.Code,
                currencyCode,
                StringComparison.Ordinal));

        return _values.Money(
            minorUnits,
            currencyCode,
            currency?.MinorUnitDigits);
    }

    public string PresentExactDecimal(decimal value)
        => ExactDecimalText.Format(value, _presentationCulture.Culture);

    public string PresentFineness(int partsPerMillion)
        => ExactDecimalText.Format(
               partsPerMillion / 1_000m,
               _presentationCulture.Culture)
           + " ‰";

    private OpeningBalanceFormMappingResult MapFinancialForm()
    {
        Input.Lots ??= [];
        var mapping = OpeningBalanceFormMapper.Map(
            Input,
            Choices!.Household.HouseholdId,
            Choices);
        AddErrors(mapping.Errors, prefixFinancialInput: true);

        if (!OpeningBalanceFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out _))
        {
            ModelState.AddModelError(
                "Input.IdempotencyKey",
                _text["Opening_Validation_IdempotencyKey"]);
            return mapping with { Command = null };
        }

        return mapping;
    }

    private async Task<bool> LoadChoicesAsync(
        CancellationToken cancellationToken)
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
                return false;
            }

            Choices = await _listChoices.ExecuteAsync(
                new ListOpeningBalanceChoicesQuery(
                    resolution.Household!.HouseholdId,
                    PageSize: ChoicePageSize),
                cancellationToken);
            return true;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException
                  or OpeningBalanceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
            return false;
        }
    }

    private void InitializeForm(string? accountId, string? assetId)
    {
        var selectedAccount = ParseAvailableId(
            accountId,
            Choices!.Accounts.Items.Select(item => item.AccountId))
            ?? (Choices.Accounts.Items.Count == 1
                ? Choices.Accounts.Items[0].AccountId
                : null);
        var selectedAsset = ParseAvailableId(
            assetId,
            Choices.Assets.Items.Select(item => item.AssetId));

        Input = new OpeningBalanceForm
        {
            PortfolioId = Choices.Portfolios.Items.Count == 1
                ? Choices.Portfolios.Items[0].PortfolioId.ToString("D")
                : null,
            AccountId = selectedAccount?.ToString("D"),
            AssetId = selectedAsset?.ToString("D"),
            AsOfDate = CurrentOperatingDate().ToString(
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture),
            IdempotencyKey = Guid.NewGuid().ToString("D")
        };

        if (SelectedAssetUsesLots)
        {
            Input.Lots.Add(new OpeningBalanceLotForm());
        }

        InstitutionInput.TypeCode = StableCodes.ToCode(InstitutionType.Broker);
        AccountInput.TypeCode = StableCodes.ToCode(AccountType.Investment);
        AssetInput.TypeCode = StableCodes.ToCode(AssetType.Equity);
        AssetInput.LotTrackingModeCode = StableCodes.ToCode(
            LotTrackingMode.Required);
        AssetInput.BaseCurrencyCode = Choices.Household.BaseCurrency.Code;
    }

    private DateOnly CurrentOperatingDate()
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(
                    _timeProvider.GetUtcNow(),
                    _presentationCulture.DisplayTimeZone)
                .DateTime);

    private void AddErrors(
        IReadOnlyList<OpeningBalanceFormError> errors,
        bool prefixFinancialInput)
    {
        foreach (var error in errors)
        {
            var fieldName = prefixFinancialInput
                && error.FieldName.Length > 0
                    ? $"Input.{error.FieldName}"
                    : error.FieldName;
            ModelState.AddModelError(fieldName, _text[error.ResourceKey]);
        }
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys
                     .Where(key => key.StartsWith(
                         prefix,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            ModelState.Remove(key);
        }
    }

    private void AddOpeningBalanceError(OpeningBalanceException exception)
    {
        RelatedTransactionId = exception.RelatedTransactionId;
        var resourceKey = exception.ErrorCode switch
        {
            OpeningBalanceErrorCodes.AlreadyExists =>
                "Opening_Error_AlreadyExists",
            OpeningBalanceErrorCodes.ScopeHasEffectiveHistory =>
                "Opening_Error_ScopeHasHistory",
            OpeningBalanceErrorCodes.LotTotalMismatch =>
                "Opening_Error_LotTotalMismatch",
            OpeningBalanceErrorCodes.DuplicateLot =>
                "Opening_Error_DuplicateLot",
            OpeningBalanceErrorCodes.AsOfDateInFuture =>
                "Opening_Error_AsOfFuture",
            OpeningBalanceErrorCodes.AcquisitionDateAfterAsOf =>
                "Opening_Error_AcquiredAfterAsOf",
            OpeningBalanceErrorCodes.ReferenceConflict =>
                "Opening_Error_ReferenceConflict",
            OpeningBalanceErrorCodes.ReferenceNotFound =>
                "Opening_Error_ReferenceNotFound",
            OpeningBalanceErrorCodes.ReferenceInactive =>
                "Opening_Error_ReferenceInactive",
            OpeningBalanceErrorCodes.AccountAssetMismatch =>
                "Opening_Error_AccountAssetMismatch",
            OpeningBalanceErrorCodes.QuantityInvalid =>
                "Opening_Error_Quantity",
            OpeningBalanceErrorCodes.CostBasisInvalid =>
                "Opening_Error_CostBasis",
            OpeningBalanceErrorCodes.GoldDetailRequired
                or OpeningBalanceErrorCodes.GoldDetailForbidden =>
                "Opening_Error_GoldDetail",
            _ => "Opening_Error_InvalidInput"
        };
        AddFormError(
            resourceKey,
            exception.Category switch
            {
                OpeningBalanceErrorCategory.NotFound => 404,
                OpeningBalanceErrorCategory.Conflict => 409,
                OpeningBalanceErrorCategory.Validation => 422,
                _ => 409
            });
    }

    private void AddFormError(string resourceKey, int statusCode)
    {
        ModelState.AddModelError(string.Empty, _text[resourceKey]);
        Response.StatusCode = statusCode;
    }

    private static Guid? ParseAvailableId(
        string? value,
        IEnumerable<Guid> available)
        => Guid.TryParseExact(value, "D", out var parsed)
           && parsed != Guid.Empty
           && available.Contains(parsed)
            ? parsed
            : null;

    private static bool IsCompatible(
        AccountType accountType,
        AssetType assetType)
        => assetType switch
        {
            AssetType.Cash or AssetType.Currency =>
                accountType is AccountType.Cash
                    or AccountType.Investment
                    or AccountType.Pension,
            AssetType.Fund or AssetType.Equity =>
                accountType is AccountType.Investment
                    or AccountType.Pension,
            AssetType.PhysicalGold =>
                accountType == AccountType.PhysicalVault,
            _ => false
        };

    private static IReadOnlyList<OpeningBalanceSelectOption> Options<T>(
        IEnumerable<T> values,
        Func<T, DisplayValue> present)
        => values
            .Select(value =>
            {
                var display = present(value);
                return new OpeningBalanceSelectOption(
                    display.TechnicalDetail!,
                    display.Text);
            })
            .ToArray();
}

public sealed record OpeningBalanceSelectOption(
    string Code,
    string Text);
