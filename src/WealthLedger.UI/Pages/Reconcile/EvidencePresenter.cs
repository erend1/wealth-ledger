using System.Globalization;
using WealthLedger.Application.Evidence;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Reconcile;

public sealed record EvidenceSummaryDisplay(
    Guid EvidenceId,
    string Label,
    DisplayValue SourceKind,
    DisplayValue EffectiveDate,
    string? Reference,
    string? Context,
    DisplayValue? AttachmentType,
    int TransactionLinkCount,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    DisplayValue CreatedAt);

public sealed record EvidenceDetailDisplay(
    Guid EvidenceId,
    string Label,
    DisplayValue SourceKind,
    DateOnly EffectiveDateValue,
    DisplayValue EffectiveDate,
    string? Reference,
    string? Note,
    string? Context,
    Guid? InstitutionId,
    Guid? AccountId,
    DisplayValue CreatedAt,
    Guid? SupersedesEvidenceId,
    Guid? SupersededByEvidenceId,
    EvidenceAttachmentDisplay? Attachment,
    IReadOnlyList<EvidenceLinkDisplay> Links)
{
    public bool IsSuperseded => SupersededByEvidenceId is not null;
}

public sealed record EvidenceAttachmentDisplay(
    string FileName,
    DisplayValue MediaType,
    string Size,
    string Sha256,
    DisplayValue CreatedAt);

public sealed record EvidenceLinkDisplay(
    Guid TransactionId,
    DisplayValue TransactionType,
    DisplayValue ExecutionDate,
    DisplayValue LinkedAt);

/// <summary>
/// Formats evidence metadata. It never reads or renders attachment bytes.
/// </summary>
public sealed class EvidencePresenter
{
    private readonly ValuePresenter _values;
    private readonly UiText _text;
    private readonly PresentationCulture _presentationCulture;

    public EvidencePresenter(
        ValuePresenter values,
        UiText text,
        PresentationCulture presentationCulture)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    private CultureInfo Culture => _presentationCulture.Culture;

    internal EvidenceSummaryDisplay PresentSummary(EvidenceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new EvidenceSummaryDisplay(
            summary.EvidenceId,
            summary.Label,
            _values.StableCode(summary.SourceKind),
            _values.BusinessDate(summary.EffectiveDate),
            summary.Reference,
            Context(summary.Context),
            summary.Attachment is null
                ? null
                : _values.StableCode(summary.Attachment.MediaType),
            summary.TransactionLinkCount,
            summary.SupersedesEvidenceId,
            summary.SupersededByEvidenceId,
            _values.UtcTimestamp(summary.CreatedAtUtc));
    }

    internal EvidenceDetailDisplay PresentDetail(EvidenceDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new EvidenceDetailDisplay(
            detail.EvidenceId,
            detail.Label,
            _values.StableCode(detail.SourceKind),
            detail.EffectiveDate,
            _values.BusinessDate(detail.EffectiveDate),
            detail.Reference,
            detail.Note,
            Context(detail.Context),
            detail.Context.InstitutionId,
            detail.Context.AccountId,
            _values.UtcTimestamp(detail.CreatedAtUtc),
            detail.SupersedesEvidenceId,
            detail.SupersededByEvidenceId,
            detail.Attachment is null
                ? null
                : new EvidenceAttachmentDisplay(
                    detail.Attachment.FileName,
                    _values.StableCode(detail.Attachment.MediaType),
                    ByteSize(detail.Attachment.ByteLength),
                    detail.Attachment.Sha256,
                    _values.UtcTimestamp(detail.Attachment.CreatedAtUtc)),
            detail.TransactionLinks
                .Select(link => new EvidenceLinkDisplay(
                    link.TransactionId,
                    _values.StableCode(link.TransactionType),
                    link.ExecutionDate is null
                        ? _values.Unknown()
                        : _values.BusinessDate(link.ExecutionDate.Value),
                    _values.UtcTimestamp(link.LinkedAtUtc)))
                .ToArray());
    }

    /// <summary>
    /// An exact byte count, so the operator can compare it with the source.
    /// </summary>
    internal string ByteSize(int byteLength)
        => string.Format(
            Culture,
            _text["Evidence_ByteSize"],
            byteLength.ToString("N0", Culture));

    private static string? Context(EvidenceContext context)
    {
        var parts = new[] { context.AccountName, context.InstitutionName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }
}
