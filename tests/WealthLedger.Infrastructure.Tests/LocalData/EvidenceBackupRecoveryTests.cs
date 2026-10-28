using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.Evidence;
using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Tests.Persistence;

namespace WealthLedger.Infrastructure.Tests.LocalData;

/// <summary>
/// Evidence lives inside the SQLite file, so the normal M004 package must
/// carry it: metadata, retained bytes, digests, links and supersession.
/// </summary>
public sealed class EvidenceBackupRecoveryTests
{
    [Fact]
    public async Task BackupVerifyAndStage_CarryEvidenceAttachmentsAndLinks()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        WealthLedgerDbContext Live()
            => SqliteLocalDataConnectionFactory.CreateContext(
                harness.DatabasePath,
                SqliteOpenMode.ReadWrite);
        var fixture = await EvidenceTestData.SeedAsync(Live);
        Guid original;
        Guid replacement;

        await using (var context = Live())
        {
            original = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-backup-original",
                EvidenceTestData.Command(
                    accountId: CoreLedgerTestData.AccountId,
                    transactionIds: [fixture.FirstPostedId],
                    attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
            replacement = (await EvidenceTestData.RecordUseCase(
                    context,
                    EvidenceTestData.Now.AddHours(1))
                .ExecuteAsync(
                    "evidence-backup-replacement",
                    EvidenceTestData.Command(
                        label: "Synthetic corrected statement",
                        supersedesEvidenceId: original,
                        transactionIds: [fixture.FirstPostedId, fixture.SecondPostedId],
                        attachment: new EvidenceAttachmentUpload(
                            "corrected.csv",
                            EvidenceMediaType.Csv,
                            EvidenceTestData.SyntheticCsv)))).EvidenceId;
        }

        var backup = await harness.CreateBackupAsync();
        Assert.True(backup.Succeeded, backup.Failure?.Message);

        var verified = await harness.Verifier.VerifyAsync(backup.Value!.FilePath);
        Assert.True(verified.Succeeded, verified.Failure?.Message);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            verified.Value!.Compatibility);
        Assert.Equal(
            "20261028065838_009_EvidencePersistence",
            verified.Value.LatestMigration);

        var target = Path.Combine(harness.RootPath, "restore-drill", "restored.db");
        var staged = await harness.RestoreStager.StageAsync(
            backup.Value.FilePath,
            target);
        Assert.True(staged.Succeeded, staged.Failure?.Message);

        await using var restored = SqliteLocalDataConnectionFactory.CreateContext(
            target,
            SqliteOpenMode.ReadOnly);
        var readStore = new EfCoreEvidenceReadStore(restored);
        var originalDetail = await new GetEvidenceUseCase(readStore)
            .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, original));
        var replacementDetail = await new GetEvidenceUseCase(readStore)
            .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, replacement));
        var originalContent = await new GetEvidenceAttachmentContentUseCase(readStore)
            .ExecuteAsync(new GetEvidenceAttachmentContentQuery(CoreLedgerTestData.HouseholdId, original));
        var replacementContent = await new GetEvidenceAttachmentContentUseCase(readStore)
            .ExecuteAsync(new GetEvidenceAttachmentContentQuery(CoreLedgerTestData.HouseholdId, replacement));
        var list = await new ListEvidenceUseCase(readStore)
            .ExecuteAsync(new ListEvidenceQuery(CoreLedgerTestData.HouseholdId));

        Assert.Equal(replacement, originalDetail.SupersededByEvidenceId);
        Assert.Equal(CoreLedgerTestData.AccountId, originalDetail.Context.AccountId);
        Assert.Equal([fixture.FirstPostedId], originalDetail.TransactionLinks.Select(x => x.TransactionId));
        Assert.Equal(original, replacementDetail.SupersedesEvidenceId);
        Assert.Equal(2, replacementDetail.TransactionLinks.Count);

        Assert.Equal(EvidenceTestData.SyntheticPdf, originalContent.Content.ToArray());
        Assert.Equal(
            EvidenceAttachment.ComputeSha256(EvidenceTestData.SyntheticPdf),
            originalContent.Sha256);
        Assert.Equal(EvidenceTestData.SyntheticPdf.Length, originalDetail.Attachment!.ByteLength);
        Assert.Equal(EvidenceTestData.SyntheticCsv, replacementContent.Content.ToArray());
        Assert.Equal(
            EvidenceAttachment.ComputeSha256(EvidenceTestData.SyntheticCsv),
            replacementDetail.Attachment!.Sha256);

        Assert.Equal(
            [replacement, original],
            list.Items.Select(x => x.EvidenceId));

        // The ledger the evidence points at came across unchanged.
        await using var live = Live();
        Assert.Equal(
            await PostedTransactionIdsAsync(live),
            await PostedTransactionIdsAsync(restored));
    }

    private static async Task<IReadOnlyList<Guid>> PostedTransactionIdsAsync(
        WealthLedgerDbContext context)
        => await context.LedgerTransactions
            .AsNoTracking()
            .Where(x => x.Status == TransactionStatus.Posted)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();
}
