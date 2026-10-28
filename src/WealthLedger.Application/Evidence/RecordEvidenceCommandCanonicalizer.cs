using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Evidence;

internal sealed record NormalizedEvidenceCommand(
    Guid HouseholdId,
    EvidenceSourceKind SourceKind,
    DateOnly EffectiveDate,
    string Label,
    Guid? InstitutionId,
    Guid? AccountId,
    string? Reference,
    string? Note,
    Guid? SupersedesEvidenceId,
    IReadOnlyList<Guid> TransactionIds,
    EvidenceAttachment? Attachment)
{
    public override string ToString() => nameof(NormalizedEvidenceCommand);
}

/// <summary>
/// Validates and normalizes evidence input before any lookup or write.
/// </summary>
/// <remarks>
/// Attachment acceptance runs here, so a rejected file never reaches
/// persistence. Transaction identities become a sorted distinct set, so the
/// same logical link set always has the same fingerprint.
/// </remarks>
internal static class RecordEvidenceCommandCanonicalizer
{
    internal const int MaximumTransactionLinksPerCommand = 100;

    internal static NormalizedEvidenceCommand Normalize(
        RecordEvidenceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.IsDefined(command.SourceKind))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.InputInvalid,
                "The evidence source kind is not supported.");
        }

        if (!EvidenceText.TryNormalizeLabel(command.Label, out var label))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.InputInvalid,
                "An evidence label is required, single-line, and limited to 256 characters.");
        }

        if (!EvidenceText.TryNormalizeReference(command.Reference, out var reference))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.InputInvalid,
                "An evidence reference is single-line and limited to 256 characters.");
        }

        if (!EvidenceText.TryNormalizeNote(command.Note, out var note))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.InputInvalid,
                "An evidence note is limited to 2000 characters without control characters.");
        }

        EnsureOptionalIdentity(command.InstitutionId);
        EnsureOptionalIdentity(command.AccountId);
        EnsureOptionalIdentity(command.SupersedesEvidenceId);

        var transactionIds = NormalizeTransactionIds(
            command.TransactionIds,
            requireAtLeastOne: false);

        var attachment = command.Attachment is null
            ? null
            : CreateAttachment(command.Attachment);

        return new NormalizedEvidenceCommand(
            command.HouseholdId,
            command.SourceKind,
            command.EffectiveDate,
            label,
            command.InstitutionId,
            command.AccountId,
            reference,
            note,
            command.SupersedesEvidenceId,
            transactionIds,
            attachment);
    }

    internal static IReadOnlyList<Guid> NormalizeTransactionIds(
        IReadOnlyList<Guid>? transactionIds,
        bool requireAtLeastOne)
    {
        transactionIds ??= [];

        if (transactionIds.Any(x => x == Guid.Empty))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.TransactionLinkInvalid,
                "A linked transaction identity cannot be empty.");
        }

        var normalized = transactionIds
            .Distinct()
            .OrderBy(x => x.ToString("D"), StringComparer.Ordinal)
            .ToArray();

        if (requireAtLeastOne && normalized.Length == 0)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.TransactionLinkInvalid,
                "At least one transaction must be linked.");
        }

        if (normalized.Length > MaximumTransactionLinksPerCommand)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.TransactionLinkLimitExceeded,
                "One evidence command can link at most 100 transactions.");
        }

        return normalized;
    }

    private static EvidenceAttachment CreateAttachment(
        EvidenceAttachmentUpload upload)
    {
        if (!Enum.IsDefined(upload.MediaType))
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.AttachmentTypeMismatch,
                "The evidence attachment media type is not supported.");
        }

        try
        {
            return EvidenceAttachment.Create(
                upload.FileName,
                upload.MediaType,
                upload.Content.Span);
        }
        catch (EvidenceAttachmentRejectedException exception)
        {
            var errorCode = exception.Reason switch
            {
                EvidenceAttachmentRejection.Empty =>
                    EvidenceErrorCodes.AttachmentEmpty,
                EvidenceAttachmentRejection.TooLarge =>
                    EvidenceErrorCodes.AttachmentTooLarge,
                EvidenceAttachmentRejection.FileNameInvalid =>
                    EvidenceErrorCodes.AttachmentFileNameInvalid,
                EvidenceAttachmentRejection.FileNameExtensionMismatch =>
                    EvidenceErrorCodes.AttachmentTypeMismatch,
                _ => EvidenceErrorCodes.AttachmentContentRejected
            };

            throw new EvidenceException(
                EvidenceErrorCategory.Validation,
                errorCode,
                exception.Message,
                exception);
        }
    }

    private static void EnsureOptionalIdentity(Guid? value)
    {
        if (value == Guid.Empty)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.InputInvalid,
                "An optional evidence identity cannot be empty when supplied.");
        }
    }
}
