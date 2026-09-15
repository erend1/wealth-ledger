using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.FundTrades;
using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class FundPurchaseModel : FundTradePageModel
{
    private const int MaximumCostRows = 16;

    private readonly PreviewFundPurchaseUseCase _preview;
    private readonly RecordFundPurchaseUseCase _record;

    public FundPurchaseModel(
        ReadyHouseholdResolver householdResolver,
        ListFundTradeChoicesUseCase listChoices,
        PreviewFundPurchaseUseCase preview,
        RecordFundPurchaseUseCase record,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
        : base(
            householdResolver,
            listChoices,
            values,
            text,
            timeProvider,
            presentationCulture)
    {
        _preview = preview
            ?? throw new ArgumentNullException(nameof(preview));
        _record = record
            ?? throw new ArgumentNullException(nameof(record));
    }

    protected override TransactionType TradeType => TransactionType.Buy;

    public FundPurchasePreview? Preview { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        InitializeForm();

        return Page();
    }

    public async Task<IActionResult> OnPostAddCostAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadChoicesAsync(cancellationToken))
        {
            return Page();
        }

        if (Input.Costs.Count >= MaximumCostRows)
        {
            AddFormError("Fund_Error_Cost", 422);
        }
        else
        {
            Input.Costs.Add(new FundTradeCostForm());
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

        var mapping =
            FundTradeFormMapper.Map(
                Input,
                TradeType,
                HouseholdId,
                Choices!);

        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);

            return Page();
        }

        // Preview writes nothing; it only derives what posting would do.
        if (await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    mapping.Purchase!,
                    cancellationToken)))
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

        var mapping =
            FundTradeFormMapper.Map(
                Input,
                TradeType,
                HouseholdId,
                Choices!);

        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);

            return Page();
        }

        var purchase = mapping.Purchase!;

        RecordFundPurchaseResult? result = null;

        var posted = await TryExecuteAsync(async () =>
            result = await _record.ExecuteAsync(
                idempotencyKey,
                new RecordFundPurchaseCommand(
                    purchase.HouseholdId,
                    purchase.PortfolioId,
                    purchase.FundAccountId,
                    purchase.FundAssetId,
                    purchase.CashAssetId,
                    purchase.FundQuantity,
                    purchase.ExecutedUnitPrice,
                    purchase.CashConsideration,
                    purchase.ExecutionDate,
                    purchase.ExternalReference,
                    purchase.Note,
                    purchase.CashAccountId,
                    purchase.OrderDate,
                    purchase.SettlementDate,
                    purchase.Costs),
                cancellationToken));

        if (!posted)
        {
            /*
             * A refused post returns to review rather than to a blank form,
             * so the reviewed figures stay on screen next to the reason.
             */
            await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    purchase,
                    cancellationToken));

            IsReview = Preview is not null;

            return Page();
        }

        // Post/Redirect/Get: refreshing the receipt creates nothing.
        return RedirectToPage(
            "/Record/FundTradeReceipt",
            new
            {
                transactionId = result!.TransactionId.ToString("D")
            });
    }
}
