using WealthLedger.Api.Contracts;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Api.Mapping;

internal static class EvidenceContractMapper
{
    internal static readonly IReadOnlyDictionary<string, EvidenceSourceKind> SourceKindCodes =
        Enum.GetValues<EvidenceSourceKind>()
            .ToDictionary(ToCode, StringComparer.Ordinal);

    internal static string EvidenceLocation(Guid householdId, Guid evidenceId)
        => $"/api/households/{householdId}/evidence/{evidenceId}";

    internal static RecordEvidenceCommand ToCommand(
        this RecordEvidenceRequest request,
        Guid householdId,
        EvidenceSourceKind sourceKind,
        EvidenceAttachmentUpload? attachment = null)
        => new(
            householdId,
            sourceKind,
            request.EffectiveDate,
            request.Label,
            request.InstitutionId,
            request.AccountId,
            request.Reference,
            request.Note,
            request.SupersedesEvidenceId,
            request.TransactionIds,
            attachment);

    internal static EvidenceSummaryResponse ToResponse(this EvidenceSummary item)
        => new(
            item.EvidenceId,
            item.HouseholdId,
            ToCode(item.SourceKind),
            item.EffectiveDate,
            item.Label,
            item.Reference,
            item.Context.ToResponse(),
            item.CreatedAtUtc,
            item.SupersedesEvidenceId,
            item.SupersededByEvidenceId,
            item.Attachment?.ToResponse(item.HouseholdId, item.EvidenceId),
            item.TransactionLinkCount);

    internal static EvidenceDetailResponse ToResponse(this EvidenceDetail item)
        => new(
            item.EvidenceId,
            item.HouseholdId,
            ToCode(item.SourceKind),
            item.EffectiveDate,
            item.Label,
            item.Reference,
            item.Note,
            item.Context.ToResponse(),
            item.CreatedAtUtc,
            item.SupersedesEvidenceId,
            item.SupersededByEvidenceId,
            item.Attachment?.ToResponse(item.HouseholdId, item.EvidenceId),
            item.TransactionLinks
                .Select(
                    link => new EvidenceTransactionLinkResponse(
                        link.TransactionId,
                        NavigationContractMapper.ToCode(link.TransactionType),
                        link.ExecutionDate,
                        link.LinkedAtUtc))
                .ToArray());

    internal static EvidenceObservationListResponse ToResponse(
        this EvidenceObservationList list)
        => new(
            list.EvidenceId,
            list.HouseholdId,
            list.EffectiveDate,
            list.SupersededByEvidenceId,
            list.Items
                .Select(
                    item => new EvidenceObservationResponse(
                        item.ObservationId,
                        item.Scope.ToResponse(),
                        item.ObservedQuantityRawE8,
                        item.ToleranceRawE8,
                        item.ObservedPieceCount,
                        item.RecordedAtUtc))
                .ToArray());

    internal static EvidenceObservationScopeResponse ToResponse(
        this EvidenceObservationScope scope)
        => new(
            scope.PortfolioId,
            scope.PortfolioCode,
            scope.PortfolioName,
            scope.AccountId,
            scope.AccountCode,
            scope.AccountName,
            scope.InstitutionId,
            scope.InstitutionCode,
            scope.InstitutionName,
            scope.AssetId,
            scope.AssetCode,
            scope.AssetName,
            NavigationContractMapper.ToCode(scope.AssetType),
            NavigationContractMapper.ToCode(scope.AssetBaseUnit));

    internal static string ToCode(EvidenceMediaType value)
        => value switch
        {
            EvidenceMediaType.Pdf => "PDF",
            EvidenceMediaType.Png => "PNG",
            EvidenceMediaType.Jpeg => "JPEG",
            EvidenceMediaType.PlainText => "PLAIN_TEXT",
            EvidenceMediaType.Csv => "CSV",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The value has no stable API code.")
        };

    internal static string ToCode(EvidenceSourceKind value)
        => value switch
        {
            EvidenceSourceKind.Statement => "STATEMENT",
            EvidenceSourceKind.Receipt => "RECEIPT",
            EvidenceSourceKind.PhysicalCount => "PHYSICAL_COUNT",
            EvidenceSourceKind.ContractOrCertificate => "CONTRACT_OR_CERTIFICATE",
            EvidenceSourceKind.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "The value has no stable API code.")
        };

    private static EvidenceContextResponse ToResponse(this EvidenceContext context)
        => new(
            context.InstitutionId,
            context.InstitutionCode,
            context.InstitutionName,
            context.AccountId,
            context.AccountCode,
            context.AccountName);

    private static EvidenceAttachmentMetadataResponse ToResponse(
        this EvidenceAttachmentMetadata attachment,
        Guid householdId,
        Guid evidenceId)
        => new(
            attachment.FileName,
            ToCode(attachment.MediaType),
            attachment.ByteLength,
            attachment.Sha256,
            attachment.CreatedAtUtc,
            $"{EvidenceLocation(householdId, evidenceId)}/attachment");
}
