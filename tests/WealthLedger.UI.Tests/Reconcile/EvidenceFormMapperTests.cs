using System.Reflection;
using Microsoft.AspNetCore.Http;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.Reconciliation;
using WealthLedger.Domain.Evidence;
using WealthLedger.UI.Pages.Reconcile;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Reconcile;

public sealed class EvidenceFormMapperTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void TryMap_MapsAnExplicitRecordCommandWithTrimmedText()
    {
        var institutionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var supersededId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");

        var mapped = EvidenceFormMapper.TryMap(
            new EvidenceForm
            {
                SourceKind = "PHYSICAL_COUNT",
                EffectiveDate = "2026-10-29",
                Label = "  Synthetic count ",
                InstitutionId = institutionId.ToString("D"),
                AccountId = accountId.ToString("D"),
                Reference = "   ",
                Note = " Synthetic note ",
                SupersedesEvidenceId = supersededId.ToString("D"),
                IdempotencyKey = key
            },
            HouseholdId,
            Today,
            attachment: null,
            out var command,
            out var idempotencyKey,
            out var errors);

        Assert.True(mapped);
        Assert.Empty(errors);
        Assert.Equal(key, idempotencyKey);
        Assert.NotNull(command);
        Assert.Equal(HouseholdId, command.HouseholdId);
        Assert.Equal(EvidenceSourceKind.PhysicalCount, command.SourceKind);
        Assert.Equal(Today, command.EffectiveDate);
        Assert.Equal("Synthetic count", command.Label);
        Assert.Equal(institutionId, command.InstitutionId);
        Assert.Equal(accountId, command.AccountId);
        Assert.Null(command.Reference);
        Assert.Equal("Synthetic note", command.Note);
        Assert.Equal(supersededId, command.SupersedesEvidenceId);
        Assert.Null(command.TransactionIds);
        Assert.Null(command.Attachment);
    }

    [Fact]
    public void TryMap_GivesEachInvalidFieldItsOwnMessage()
    {
        var mapped = EvidenceFormMapper.TryMap(
            new EvidenceForm
            {
                SourceKind = "BANK_STATEMENT",
                EffectiveDate = "2026-10-30",
                Label = new string('l', 257),
                AccountId = "not-a-guid",
                Reference = new string('r', 257),
                Note = new string('n', 2_001),
                IdempotencyKey = "replayed"
            },
            HouseholdId,
            Today,
            attachment: null,
            out var command,
            out var key,
            out var errors);

        Assert.False(mapped);
        Assert.Null(command);
        Assert.Null(key);
        Assert.Equal(
            [
                new EvidenceFormError("SourceKind", "Evidence_Validation_SourceKind"),
                new EvidenceFormError("EffectiveDate", "M010Error_EVIDENCE_EFFECTIVE_DATE_IN_FUTURE"),
                new EvidenceFormError("Label", "Evidence_Validation_LabelLength"),
                new EvidenceFormError("Reference", "Evidence_Validation_ReferenceLength"),
                new EvidenceFormError("Note", "Evidence_Validation_NoteLength"),
                new EvidenceFormError("AccountId", "Evidence_Validation_Selection"),
                new EvidenceFormError(string.Empty, "Evidence_Error_FormExpired")
            ],
            errors);
    }

    [Theory]
    [InlineData(null, "Evidence_Validation_Date")]
    [InlineData("31.01.2026", "Evidence_Validation_Date")]
    public void TryMap_RequiresAnIsoEffectiveDateAndALabel(
        string? effectiveDate,
        string dateKey)
    {
        EvidenceFormMapper.TryMap(
            new EvidenceForm
            {
                SourceKind = "STATEMENT",
                EffectiveDate = effectiveDate,
                Label = " ",
                IdempotencyKey = Guid.NewGuid().ToString("D")
            },
            HouseholdId,
            Today,
            attachment: null,
            out _,
            out _,
            out var errors);

        Assert.Equal(
            [
                new EvidenceFormError("EffectiveDate", dateKey),
                new EvidenceFormError("Label", "Evidence_Validation_LabelRequired")
            ],
            errors);
    }

    [Fact]
    public void ErrorText_DescribesEveryEvidenceAndReconciliationCode()
    {
        var text = new UiText(PresentationCulture.CreateDefault());
        var codes = typeof(EvidenceErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(ReconciliationErrorCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(codes);

        foreach (var code in codes)
        {
            var message = text[ReconcileErrorText.ResourceKey(code)];

            Assert.False(string.IsNullOrWhiteSpace(message), code);
            Assert.DoesNotContain(code, message, StringComparison.Ordinal);
        }

        Assert.Equal(
            StatusCodes.Status404NotFound,
            ReconcileErrorText.StatusCode(EvidenceErrorCategory.NotFound));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            ReconcileErrorText.StatusCode(ReconciliationErrorCategory.Conflict));
        Assert.Equal(
            StatusCodes.Status422UnprocessableEntity,
            ReconcileErrorText.StatusCode(ReconciliationErrorCategory.Validation));
    }

    [Fact]
    public void EvidencePresenter_ShowsExactByteCountsAndNoBytes()
    {
        var culture = PresentationCulture.CreateDefault();
        var presenter = new EvidencePresenter(
            new ValuePresenter(culture),
            new UiText(culture),
            culture);
        var detail = presenter.PresentDetail(
            new EvidenceDetail(
                Guid.NewGuid(),
                HouseholdId,
                EvidenceSourceKind.Statement,
                new DateOnly(2026, 1, 31),
                "Synthetic statement",
                Reference: null,
                Note: null,
                new EvidenceContext(null, null, "Synthetic Bank", null, null, "Synthetic Account"),
                DateTimeOffset.UnixEpoch,
                SupersedesEvidenceId: null,
                SupersededByEvidenceId: Guid.NewGuid(),
                new EvidenceAttachmentMetadata(
                    "synthetic.pdf",
                    EvidenceMediaType.Pdf,
                    1_234_567,
                    new string('a', 64),
                    DateTimeOffset.UnixEpoch),
                []));

        Assert.True(detail.IsSuperseded);
        Assert.Equal("Synthetic Account · Synthetic Bank", detail.Context);
        Assert.Equal("1.234.567 bayt", detail.Attachment!.Size);
        Assert.Equal("PDF belgesi", detail.Attachment.MediaType.Text);
    }
}
