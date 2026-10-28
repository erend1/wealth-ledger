using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

/// <summary>
/// Household-scoped evidence reads. List and detail projections never select
/// attachment bytes; only <see cref="ReadAttachmentAsync"/> does.
/// </summary>
public sealed class EfCoreEvidenceReadStore : IEvidenceReadStore
{
    private readonly WealthLedgerDbContext _dbContext;

    public EfCoreEvidenceReadStore(WealthLedgerDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<bool> HouseholdExistsAsync(
        Guid householdId,
        CancellationToken cancellationToken = default)
        => _dbContext.Households
            .AsNoTracking()
            .AnyAsync(row => row.Id == householdId, cancellationToken);

    public async Task<IReadOnlyList<EvidenceSummary>> ListAsync(
        Guid householdId,
        int take,
        EvidenceCursorKey? after,
        CancellationToken cancellationToken = default)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        var records = _dbContext.EvidenceRecords
            .AsNoTracking()
            .Where(record => record.HouseholdId == householdId);

        if (after is not null)
        {
            var afterCreatedAtUtc = after.CreatedAtUtc.UtcDateTime;

            records = records.Where(
                record =>
                    record.EffectiveDate < after.EffectiveDate
                    || (record.EffectiveDate == after.EffectiveDate
                        && record.CreatedAtUtc < afterCreatedAtUtc)
                    || (record.EffectiveDate == after.EffectiveDate
                        && record.CreatedAtUtc == afterCreatedAtUtc
                        && record.Id.CompareTo(after.EvidenceId) < 0));
        }

        var rows = await Project(records)
            .OrderByDescending(row => row.EffectiveDate)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new EvidenceSummary(
                row.Id,
                row.HouseholdId,
                row.SourceKind,
                row.EffectiveDate,
                row.Label,
                row.Reference,
                ToContext(row),
                ToUtc(row.CreatedAtUtc),
                row.SupersedesEvidenceId,
                row.SupersededByEvidenceId,
                ToAttachment(row),
                row.TransactionLinkCount))
            .ToArray();
    }

    public async Task<EvidenceDetail?> GetAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        var row = await Project(
                _dbContext.EvidenceRecords
                    .AsNoTracking()
                    .Where(record => record.HouseholdId == householdId
                        && record.Id == evidenceId))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var links = await (
                from link in _dbContext.EvidenceTransactionLinks.AsNoTracking()
                join transaction in _dbContext.LedgerTransactions.AsNoTracking()
                    on link.LedgerTransactionId equals transaction.Id
                where link.EvidenceId == evidenceId
                orderby transaction.ExecutionDate descending,
                    link.LedgerTransactionId
                select new
                {
                    link.LedgerTransactionId,
                    transaction.Type,
                    transaction.ExecutionDate,
                    link.LinkedAtUtc
                })
            .ToListAsync(cancellationToken);

        return new EvidenceDetail(
            row.Id,
            row.HouseholdId,
            row.SourceKind,
            row.EffectiveDate,
            row.Label,
            row.Reference,
            row.Note,
            ToContext(row),
            ToUtc(row.CreatedAtUtc),
            row.SupersedesEvidenceId,
            row.SupersededByEvidenceId,
            ToAttachment(row),
            links
                .Select(link => new EvidenceTransactionLinkItem(
                    link.LedgerTransactionId,
                    link.Type,
                    link.ExecutionDate,
                    ToUtc(link.LinkedAtUtc)))
                .ToArray());
    }

    public async Task<StoredEvidenceAttachment?> ReadAttachmentAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        var row = await (
                from attachment in _dbContext.EvidenceAttachments.AsNoTracking()
                join record in _dbContext.EvidenceRecords.AsNoTracking()
                    on attachment.EvidenceId equals record.Id
                where record.HouseholdId == householdId
                    && record.Id == evidenceId
                select attachment)
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new StoredEvidenceAttachment(
                row.EvidenceId,
                row.FileName,
                row.MediaType,
                row.ByteLength,
                row.Sha256,
                row.Content);
    }

    private IQueryable<EvidenceProjection> Project(
        IQueryable<EvidenceRecordRow> records)
        => from record in records
           join institution in _dbContext.Institutions.AsNoTracking()
               on record.InstitutionId equals (Guid?)institution.Id into institutions
           from institution in institutions.DefaultIfEmpty()
           join account in _dbContext.Accounts.AsNoTracking()
               on record.AccountId equals (Guid?)account.Id into accounts
           from account in accounts.DefaultIfEmpty()
           join attachment in _dbContext.EvidenceAttachments.AsNoTracking()
               on record.Id equals attachment.EvidenceId into attachments
           from attachment in attachments.DefaultIfEmpty()
           select new EvidenceProjection
           {
               Id = record.Id,
               HouseholdId = record.HouseholdId,
               SourceKind = record.SourceKind,
               EffectiveDate = record.EffectiveDate,
               Label = record.Label,
               Reference = record.Reference,
               Note = record.Note,
               InstitutionId = record.InstitutionId,
               InstitutionCode = institution == null ? null : institution.Code,
               InstitutionName = institution == null ? null : institution.Name,
               AccountId = record.AccountId,
               AccountCode = account == null ? null : account.Code,
               AccountName = account == null ? null : account.Name,
               CreatedAtUtc = record.CreatedAtUtc,
               SupersedesEvidenceId = record.SupersedesEvidenceId,
               SupersededByEvidenceId = _dbContext.EvidenceRecords
                   .Where(successor => successor.SupersedesEvidenceId == record.Id)
                   .Select(successor => (Guid?)successor.Id)
                   .FirstOrDefault(),
               HasAttachment = attachment != null,
               AttachmentFileName = attachment == null ? null : attachment.FileName,
               AttachmentMediaType = attachment == null
                   ? null
                   : (EvidenceMediaType?)attachment.MediaType,
               AttachmentByteLength = attachment == null
                   ? null
                   : (long?)attachment.ByteLength,
               AttachmentSha256 = attachment == null ? null : attachment.Sha256,
               AttachmentCreatedAtUtc = attachment == null
                   ? null
                   : (DateTime?)attachment.CreatedAtUtc,
               TransactionLinkCount = _dbContext.EvidenceTransactionLinks
                   .Count(link => link.EvidenceId == record.Id)
           };

    private static EvidenceContext ToContext(EvidenceProjection row)
        => new(
            row.InstitutionId,
            row.InstitutionCode,
            row.InstitutionName,
            row.AccountId,
            row.AccountCode,
            row.AccountName);

    private static EvidenceAttachmentMetadata? ToAttachment(
        EvidenceProjection row)
    {
        if (!row.HasAttachment)
        {
            return null;
        }

        if (row.AttachmentFileName is null
            || row.AttachmentMediaType is null
            || row.AttachmentByteLength is null
            || row.AttachmentSha256 is null
            || row.AttachmentCreatedAtUtc is null
            || row.AttachmentByteLength is < 1 or > EvidenceAttachmentPolicy.MaximumByteLength)
        {
            throw new EvidencePersistenceException(
                "A persisted evidence attachment has an unsupported shape.");
        }

        return new EvidenceAttachmentMetadata(
            row.AttachmentFileName,
            row.AttachmentMediaType.Value,
            (int)row.AttachmentByteLength.Value,
            row.AttachmentSha256,
            ToUtc(row.AttachmentCreatedAtUtc.Value));
    }

    public async Task<EvidenceObservationList?> ListObservationsAsync(
        Guid householdId,
        Guid evidenceId,
        CancellationToken cancellationToken = default)
    {
        var evidence = await _dbContext.EvidenceRecords
            .AsNoTracking()
            .Where(record => record.Id == evidenceId
                             && record.HouseholdId == householdId)
            .Select(record => new
            {
                record.Id,
                record.HouseholdId,
                record.EffectiveDate,
                SupersededByEvidenceId = _dbContext.EvidenceRecords
                    .Where(successor => successor.SupersedesEvidenceId == record.Id)
                    .Select(successor => (Guid?)successor.Id)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (evidence is null)
        {
            return null;
        }

        var rows = await (
                from observation in _dbContext.EvidenceObservations.AsNoTracking()
                join portfolio in _dbContext.Portfolios.AsNoTracking()
                    on observation.PortfolioId equals portfolio.Id
                join account in _dbContext.Accounts.AsNoTracking()
                    on observation.AccountId equals account.Id
                join asset in _dbContext.Assets.AsNoTracking()
                    on observation.AssetId equals asset.Id
                join institution in _dbContext.Institutions.AsNoTracking()
                    on account.InstitutionId equals (Guid?)institution.Id
                    into accountInstitutions
                from institution in accountInstitutions.DefaultIfEmpty()
                where observation.EvidenceId == evidenceId
                orderby observation.RecordedAtUtc, observation.Id
                select new
                {
                    observation.Id,
                    observation.ObservedQuantityE8,
                    observation.ToleranceE8,
                    observation.ObservedPieceCount,
                    observation.RecordedAtUtc,
                    Scope = new EvidenceObservationScope(
                        portfolio.Id,
                        portfolio.Code,
                        portfolio.Name,
                        account.Id,
                        account.Code,
                        account.Name,
                        institution == null ? null : (Guid?)institution.Id,
                        institution == null ? null : institution.Code,
                        institution == null ? null : institution.Name,
                        asset.Id,
                        asset.Code,
                        asset.Name,
                        asset.Type,
                        asset.BaseUnit)
                })
            .ToListAsync(cancellationToken);

        return new EvidenceObservationList(
            evidence.Id,
            evidence.HouseholdId,
            evidence.EffectiveDate,
            evidence.SupersededByEvidenceId,
            rows
                .Select(row => new EvidenceObservationItem(
                    row.Id,
                    row.Scope,
                    row.ObservedQuantityE8,
                    row.ToleranceE8,
                    row.ObservedPieceCount,
                    ToUtc(row.RecordedAtUtc)))
                .ToArray());
    }

    private static DateTimeOffset ToUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class EvidenceProjection
    {
        public Guid Id { get; init; }

        public Guid HouseholdId { get; init; }

        public EvidenceSourceKind SourceKind { get; init; }

        public DateOnly EffectiveDate { get; init; }

        public string Label { get; init; } = null!;

        public string? Reference { get; init; }

        public string? Note { get; init; }

        public Guid? InstitutionId { get; init; }

        public string? InstitutionCode { get; init; }

        public string? InstitutionName { get; init; }

        public Guid? AccountId { get; init; }

        public string? AccountCode { get; init; }

        public string? AccountName { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public Guid? SupersedesEvidenceId { get; init; }

        public Guid? SupersededByEvidenceId { get; init; }

        public bool HasAttachment { get; init; }

        public string? AttachmentFileName { get; init; }

        public EvidenceMediaType? AttachmentMediaType { get; init; }

        public long? AttachmentByteLength { get; init; }

        public string? AttachmentSha256 { get; init; }

        public DateTime? AttachmentCreatedAtUtc { get; init; }

        public int TransactionLinkCount { get; init; }
    }
}
