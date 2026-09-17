using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Record;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(supportsPost: true, LocalStartupMode.Ready)]
public sealed class PhysicalGoldPurchaseModel : PhysicalGoldPageModel
{
    private const int MaximumCostRows = 16;
    private readonly PreviewPhysicalGoldPurchaseUseCase _preview;
    private readonly RecordPhysicalGoldPurchaseUseCase _record;

    public PhysicalGoldPurchaseModel(
        ReadyHouseholdResolver householdResolver,
        ListPhysicalGoldChoicesUseCase listChoices,
        GetPhysicalGoldCustodyInventoryUseCase getCustody,
        PreviewPhysicalGoldPurchaseUseCase preview,
        RecordPhysicalGoldPurchaseUseCase record,
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
        => PhysicalGoldWorkflow.Purchase;

    public PhysicalGoldPurchasePreview? Preview { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (await LoadContextAsync(cancellationToken))
        {
            InitializeForm();
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

        var mapping = PhysicalGoldFormMapper.MapPurchase(
            Input,
            HouseholdId,
            Choices!);
        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);
            return Page();
        }

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

        var mapping = PhysicalGoldFormMapper.MapPurchase(
            Input,
            HouseholdId,
            Choices!);
        if (!mapping.Succeeded)
        {
            ApplyMappingErrors(mapping.Errors);
            return Page();
        }

        RecordPhysicalGoldPurchaseResult? result = null;
        var posted = await TryExecuteAsync(async () =>
            result = await _record.ExecuteAsync(
                idempotencyKey,
                mapping.Purchase!,
                cancellationToken));
        if (!posted)
        {
            await TryExecuteAsync(async () =>
                Preview = await _preview.ExecuteAsync(
                    mapping.Purchase!,
                    cancellationToken));
            IsReview = Preview is not null;
            return Page();
        }

        return RedirectToPage(
            "/Record/PhysicalGoldReceipt",
            new { transactionId = result!.TransactionId.ToString("D") });
    }
}
