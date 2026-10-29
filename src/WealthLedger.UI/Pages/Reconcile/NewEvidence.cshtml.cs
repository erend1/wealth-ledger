using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Common;
using WealthLedger.Domain.Evidence;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

/// <summary>
/// Records one evidence record, optionally retaining one bounded source file.
/// </summary>
/// <remarks>
/// <para>
/// This is one explicit step without a review screen: a browser cannot carry
/// a chosen file through a review round trip. A page-issued idempotency key
/// makes a double submit return the first record instead of a second one.
/// </para>
/// <para>
/// The request body is bounded to the largest retained file plus the form
/// fields. The declared file type only selects the retained format; the
/// Application attachment policy still checks extension and content.
/// </para>
/// </remarks>
[AutoValidateAntiforgeryToken]
[RequestSizeLimit(MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.Ready)]
public sealed class NewEvidenceModel : ReconcilePageModel
{
    // The largest retained file plus room for the text fields and the
    // multipart envelope.
    internal const int MaximumRequestBytes =
        EvidenceAttachmentPolicy.MaximumByteLength + 64 * 1024;

    private readonly ScopeChoiceLoader _choiceLoader;
    private readonly RecordEvidenceUseCase _record;
    private readonly ValuePresenter _values;
    private readonly TimeProvider _timeProvider;

    public NewEvidenceModel(
        ReadyHouseholdResolver householdResolver,
        ScopeChoiceLoader choiceLoader,
        RecordEvidenceUseCase record,
        ValuePresenter values,
        UiText text,
        TimeProvider timeProvider)
        : base(householdResolver, text)
    {
        _choiceLoader = choiceLoader
            ?? throw new ArgumentNullException(nameof(choiceLoader));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    [BindProperty]
    public EvidenceForm Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Attachment { get; set; }

    public ScopeChoices? Choices { get; private set; }

    public IReadOnlyList<(string Code, DisplayValue Label)> SourceKindChoices
        => EvidenceFormMapper.SourceKinds
            .Select(choice => (choice.Code, _values.StableCode(choice.Kind)))
            .ToArray();

    public string Today
        => OperatingDate.Today(_timeProvider).ToString("yyyy-MM-dd");

    public ScopeSelect Select(string name)
    {
        var choices = Choices ?? new ScopeChoices([], [], [], []);

        return name switch
        {
            "Input.InstitutionId" => new ScopeSelect(
                name,
                Text["Evidence_InstitutionLabel"],
                Text["Evidence_NoContextOption"],
                Text["Scope_InactiveMarker"],
                ScopeChoiceLoader.InstitutionOptions(choices),
                Input.InstitutionId,
                Text["Evidence_ContextHelp"],
                "Input_InstitutionId"),
            "Input.AccountId" => new ScopeSelect(
                name,
                Text["Evidence_AccountLabel"],
                Text["Evidence_NoContextOption"],
                Text["Scope_InactiveMarker"],
                ScopeChoiceLoader.AccountOptions(choices),
                Input.AccountId,
                Id: "Input_AccountId"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    public async Task<IActionResult> OnGetAsync(
        string? supersedes,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }

        Input.EffectiveDate ??= Today;
        Input.SourceKind ??= StableCodes.ToCode(EvidenceSourceKind.Statement);
        Input.IdempotencyKey ??= Guid.NewGuid().ToString("D");

        if (Guid.TryParseExact(supersedes, "D", out var supersededId)
            && supersededId != Guid.Empty)
        {
            Input.SupersedesEvidenceId = supersededId.ToString("D");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return Page();
        }

        // Binding records entries under property names; the messages below
        // are keyed by the ids of the controls they name.
        ModelState.Clear();

        var attachment = await ReadAttachmentAsync(cancellationToken);

        if (!EvidenceFormMapper.TryMap(
                Input,
                HouseholdId,
                OperatingDate.Today(_timeProvider),
                attachment,
                out var command,
                out var idempotencyKey,
                out var errors)
            || !ModelState.IsValid)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(FieldId(error.Field), Text[error.ResourceKey]);
            }

            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        try
        {
            var result = await _record.ExecuteAsync(
                idempotencyKey!,
                command!,
                cancellationToken);

            return RedirectToPage(
                "/Reconcile/Evidence",
                new { evidenceId = result.EvidenceId.ToString("D") });
        }
        catch (EvidenceException exception)
        {
            AddFormError(exception);
        }
        catch (IdempotencyConflictException)
        {
            AddFormError("Evidence_Error_IdempotencyConflict", StatusCodes.Status409Conflict);
        }
        catch (Exception exception)
            when (exception is ArgumentException or DomainRuleViolationException)
        {
            AddFormError("M010Error_EVIDENCE_INPUT_INVALID", StatusCodes.Status422UnprocessableEntity);
        }
        catch (EvidencePersistenceException)
        {
            AddFormError("Evidence_Error_Persistence", StatusCodes.Status409Conflict);
        }

        return Page();
    }

    /// <summary>
    /// Reads the optional file into memory after its size and declared type
    /// are checked. No file means metadata-only evidence.
    /// </summary>
    private async Task<EvidenceAttachmentUpload?> ReadAttachmentAsync(
        CancellationToken cancellationToken)
    {
        if (Attachment is null
            || (Attachment.Length == 0 && string.IsNullOrWhiteSpace(Attachment.FileName)))
        {
            return null;
        }

        if (Attachment.Length > EvidenceAttachmentPolicy.MaximumByteLength)
        {
            ModelState.AddModelError(
                "Attachment",
                Text["M010Error_EVIDENCE_ATTACHMENT_TOO_LARGE"]);
            return null;
        }

        if (!EvidenceUploadMediaTypes.TryMap(Attachment.ContentType, out var mediaType))
        {
            ModelState.AddModelError(
                "Attachment",
                Text["Evidence_Validation_AttachmentType"]);
            return null;
        }

        var content = new byte[Attachment.Length];
        await using var stream = Attachment.OpenReadStream();
        await stream.ReadExactlyAsync(content, cancellationToken);

        return new EvidenceAttachmentUpload(
            Attachment.FileName,
            mediaType,
            content);
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (!await ResolveHouseholdAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            Choices = await _choiceLoader.LoadAsync(HouseholdId, cancellationToken);
            return true;
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            Unavailable();
            return false;
        }
    }

    private static string FieldId(string field)
        => string.IsNullOrEmpty(field) ? string.Empty : "Input_" + field;
}
