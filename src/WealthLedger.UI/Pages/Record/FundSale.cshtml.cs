using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
public sealed class FundSaleModel : FundTradePageModel
{
    private const int MaximumCostRows = 16;

    private readonly PreviewFundSaleUseCase _preview;
    private readonly RecordFundSaleUseCase _record;

    public FundSaleModel(
        ReadyHouseholdResolver householdResolver,
        ListFundTradeChoicesUseCase listChoices,
        PreviewFundSaleUseCase preview,
        RecordFundSaleUseCase record,
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

    protected override TransactionType TradeType => TransactionType.Sell;

    public FundSalePreview? Preview { get; private set; }

    /// <summary>
    /// True when the reviewed plan no longer matches current holdings.
    /// </summary>
    /// <remarks>
    /// The user must review again, with a new command identity, rather than
    /// posting a plan that would consume different lots at different costs
    /// from the ones they approved.
    /// </remarks>
    public bool RequiresFreshReview { get; private set; }

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

        if (await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    mapping.Sale!,
                    cancellationToken)))
        {
            IsReview = true;
            CarryReviewedPlanIntoForm();
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

        RecordFundSaleResult? result = null;

        var posted = await TryExecuteAsync(async () =>
            result = await _record.ExecuteAsync(
                idempotencyKey,
                mapping.Sale!,
                cancellationToken));

        if (!posted)
        {
            await RecoverAfterRefusedPostAsync(
                mapping.Sale!,
                cancellationToken);

            return Page();
        }

        return RedirectToPage(
            "/Record/FundTradeReceipt",
            new
            {
                transactionId = result!.TransactionId.ToString("D")
            });
    }

    /// <summary>
    /// Re-derives the plan after a refused post so the page shows what would
    /// happen now, not what was reviewed before.
    /// </summary>
    private async Task RecoverAfterRefusedPostAsync(
        FundSaleCommand sale,
        CancellationToken cancellationToken)
    {
        RequiresFreshReview =
            Response.StatusCode == StatusCodes.Status409Conflict;

        await TryExecuteAsync(async () =>
            Preview = await _preview.ExecuteAsync(
                sale with
                {
                    ReviewedPlan = null,
                    ReviewedPlanFingerprint = null
                },
                cancellationToken));

        if (Preview is null)
        {
            return;
        }

        IsReview = true;
        CarryReviewedPlanIntoForm();

        if (RequiresFreshReview)
        {
            /*
             * A stale plan needs a new command identity. Reusing the old key
             * would make the fresh review look like a retry of the refused
             * attempt.
             */
            Input.IdempotencyKey = Guid.NewGuid().ToString("D");
        }
    }

    /// <summary>
    /// Puts the plan the user is looking at into hidden fields, so the post
    /// carries exactly what was displayed.
    /// </summary>
    private void CarryReviewedPlanIntoForm()
    {
        if (Preview is null)
        {
            return;
        }

        Input.ReviewedPlanFingerprint = Preview.PlanFingerprint;

        Input.ReviewedPlan =
            Preview.Plan
                .Select(line =>
                    new ReviewedPlanLineForm
                    {
                        AssetLotId = line.AssetLotId.ToString("D"),
                        QuantityRawE8 =
                            line.ConsumedQuantityRawE8.ToString(
                                System.Globalization.CultureInfo
                                    .InvariantCulture)
                    })
                .ToList();

        // The bound values were replaced, so the previous round's values must
        // not be redisplayed from model state.
        ModelState.Remove("Input.ReviewedPlanFingerprint");

        for (var index = 0; index < Input.ReviewedPlan.Count; index++)
        {
            ModelState.Remove(
                $"Input.ReviewedPlan[{index}].AssetLotId");

            ModelState.Remove(
                $"Input.ReviewedPlan[{index}].QuantityRawE8");
        }

        ModelState.Remove("Input.IdempotencyKey");
    }
}
