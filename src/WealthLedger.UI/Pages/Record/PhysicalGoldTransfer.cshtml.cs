using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(supportsPost: true, LocalStartupMode.Ready)]
public sealed class PhysicalGoldTransferModel : PhysicalGoldPageModel
{
    private const int MaximumCostRows = 16;
    private readonly PreviewPhysicalGoldTransferUseCase _preview;
    private readonly RecordPhysicalGoldTransferUseCase _record;

    public PhysicalGoldTransferModel(
        ReadyHouseholdResolver householdResolver,
        ListPhysicalGoldChoicesUseCase listChoices,
        GetPhysicalGoldCustodyInventoryUseCase getCustody,
        PreviewPhysicalGoldTransferUseCase preview,
        RecordPhysicalGoldTransferUseCase record,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider,
        PresentationCulture presentationCulture)
        : base(
            householdResolver,
            listChoices,
            getCustody,
            values,
            text,
            timeProvider,
            presentationCulture)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    protected override PhysicalGoldWorkflow Workflow
        => PhysicalGoldWorkflow.Transfer;

    public PhysicalGoldTransferPreview? Preview { get; private set; }

    public bool RequiresFreshReview { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await LoadContextAsync(cancellationToken))
        {
            InitializeForm();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLoadLotsAsync(
        CancellationToken cancellationToken)
    {
        if (await LoadContextAsync(cancellationToken))
        {
            SynchronizeSelectedLotRows(reset: true);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddCostAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadContextAsync(cancellationToken))
        {
            return Page();
        }

        if (Input.Costs.Count >= MaximumCostRows)
        {
            AddFormError("Gold_Error_Cost", 422);
        }
        else
        {
            Input.Costs.Add(new PhysicalGoldCostForm());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadContextAsync(cancellationToken))
        {
            return Page();
        }

        Input.ReviewedPlanFingerprint = null;
        ModelState.Remove("Input.ReviewedPlanFingerprint");
        var mapping = Map();
        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);
            return Page();
        }

        if (await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    mapping.Transfer!,
                    cancellationToken)))
        {
            IsReview = true;
            CarryPlan();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPostAsync(
        CancellationToken cancellationToken)
    {
        if (!await LoadContextAsync(cancellationToken))
        {
            return Page();
        }

        if (!PhysicalGoldFormMapper.TryParseIdempotencyKey(
                Input.IdempotencyKey,
                out var idempotencyKey))
        {
            AddFormError("Gold_Error_InvalidInput", 422);
            return Page();
        }

        var mapping = Map();
        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);
            return Page();
        }

        RecordPhysicalGoldActivityResult? result = null;
        var posted = await TryExecuteAsync(async () =>
            result = await _record.ExecuteAsync(
                idempotencyKey,
                mapping.Transfer!,
                cancellationToken));
        if (!posted)
        {
            RequiresFreshReview = Response.StatusCode ==
                StatusCodes.Status409Conflict;
            var refreshed = mapping.Transfer! with
            {
                ReviewedPlanFingerprint = null
            };
            await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    refreshed,
                    cancellationToken));
            if (Preview is not null)
            {
                IsReview = true;
                CarryPlan();
            }

            if (RequiresFreshReview)
            {
                Input.IdempotencyKey = Guid.NewGuid().ToString("D");
                ModelState.Remove("Input.IdempotencyKey");
            }

            return Page();
        }

        return RedirectToPage(
            "/Record/PhysicalGoldReceipt",
            new { transactionId = result!.TransactionId.ToString("D") });
    }

    private PhysicalGoldFormMappingResult Map()
        => PhysicalGoldFormMapper.MapTransfer(
            Input,
            HouseholdId,
            Choices!,
            Custody);

    private void CarryPlan()
    {
        if (Preview is null)
        {
            return;
        }

        Input.ReviewedPlanFingerprint = Preview.PlanFingerprint;
        ModelState.Remove("Input.ReviewedPlanFingerprint");
    }
}
