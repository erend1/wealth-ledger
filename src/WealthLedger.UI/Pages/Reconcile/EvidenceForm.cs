using System.Globalization;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

public sealed class EvidenceForm
{
    public string? SourceKind { get; set; }

    public string? EffectiveDate { get; set; }

    public string? Label { get; set; }

    public string? InstitutionId { get; set; }

    public string? AccountId { get; set; }

    public string? Reference { get; set; }

    public string? Note { get; set; }

    public string? SupersedesEvidenceId { get; set; }

    public string? IdempotencyKey { get; set; }
}

public sealed record EvidenceFormError(string Field, string ResourceKey);

/// <summary>
/// Maps the evidence capture form onto an explicit record command.
/// </summary>
/// <remarks>
/// The checks here only give each field its own message. The Application
/// use case still validates every rule, including the context and
/// supersession lookups, before anything is stored.
/// </remarks>
public static class EvidenceFormMapper
{
    public static bool TryMap(
        EvidenceForm form,
        Guid householdId,
        DateOnly today,
        EvidenceAttachmentUpload? attachment,
        out RecordEvidenceCommand? command,
        out string? idempotencyKey,
        out IReadOnlyList<EvidenceFormError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);

        var found = new List<EvidenceFormError>();
        var sourceKind = ParseSourceKind(form.SourceKind, found);
        var effectiveDate = ParseEffectiveDate(form.EffectiveDate, today, found);
        var label = form.Label?.Trim();

        if (string.IsNullOrEmpty(label))
        {
            found.Add(new EvidenceFormError(nameof(form.Label), "Evidence_Validation_LabelRequired"));
        }
        else if (label.Length > EvidenceText.MaximumLabelLength)
        {
            found.Add(new EvidenceFormError(nameof(form.Label), "Evidence_Validation_LabelLength"));
        }

        if (form.Reference?.Trim().Length > EvidenceText.MaximumReferenceLength)
        {
            found.Add(new EvidenceFormError(nameof(form.Reference), "Evidence_Validation_ReferenceLength"));
        }

        if (form.Note?.Trim().Length > EvidenceText.MaximumNoteLength)
        {
            found.Add(new EvidenceFormError(nameof(form.Note), "Evidence_Validation_NoteLength"));
        }

        var institutionId = ParseOptionalIdentity(
            form.InstitutionId,
            nameof(form.InstitutionId),
            found);
        var accountId = ParseOptionalIdentity(
            form.AccountId,
            nameof(form.AccountId),
            found);
        var supersedesId = ParseOptionalIdentity(
            form.SupersedesEvidenceId,
            nameof(form.SupersedesEvidenceId),
            found);

        if (!TryParseIdempotencyKey(form.IdempotencyKey, out var key))
        {
            found.Add(new EvidenceFormError(string.Empty, "Evidence_Error_FormExpired"));
        }

        errors = found;

        if (found.Count > 0)
        {
            command = null;
            idempotencyKey = null;
            return false;
        }

        command = new RecordEvidenceCommand(
            householdId,
            sourceKind!.Value,
            effectiveDate!.Value,
            label!,
            institutionId,
            accountId,
            Blank(form.Reference),
            Blank(form.Note),
            supersedesId,
            TransactionIds: null,
            attachment);
        idempotencyKey = key;
        return true;
    }

    public static IReadOnlyList<(string Code, EvidenceSourceKind Kind)> SourceKinds { get; } =
        Enum.GetValues<EvidenceSourceKind>()
            .Select(kind => (StableCodes.ToCode(kind), kind))
            .ToArray();

    /// <summary>
    /// A retry identity is a page-issued GUID; anything else means the form
    /// was not the one this page rendered.
    /// </summary>
    public static bool TryParseIdempotencyKey(string? value, out string key)
    {
        key = string.Empty;

        if (!Guid.TryParseExact(value, "D", out var parsed)
            || parsed == Guid.Empty)
        {
            return false;
        }

        key = parsed.ToString("D");
        return true;
    }

    private static EvidenceSourceKind? ParseSourceKind(
        string? value,
        List<EvidenceFormError> errors)
    {
        var code = value?.Trim();

        foreach (var (candidate, kind) in SourceKinds)
        {
            if (string.Equals(candidate, code, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        errors.Add(new EvidenceFormError(
            nameof(EvidenceForm.SourceKind),
            "Evidence_Validation_SourceKind"));
        return null;
    }

    private static DateOnly? ParseEffectiveDate(
        string? value,
        DateOnly today,
        List<EvidenceFormError> errors)
    {
        if (!DateOnly.TryParseExact(
                value?.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            errors.Add(new EvidenceFormError(
                nameof(EvidenceForm.EffectiveDate),
                "Evidence_Validation_Date"));
            return null;
        }

        if (parsed > today)
        {
            errors.Add(new EvidenceFormError(
                nameof(EvidenceForm.EffectiveDate),
                "M010Error_EVIDENCE_EFFECTIVE_DATE_IN_FUTURE"));
            return null;
        }

        return parsed;
    }

    private static Guid? ParseOptionalIdentity(
        string? value,
        string field,
        List<EvidenceFormError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParseExact(value.Trim(), "D", out var parsed)
            && parsed != Guid.Empty)
        {
            return parsed;
        }

        errors.Add(new EvidenceFormError(field, "Evidence_Validation_Selection"));
        return null;
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
