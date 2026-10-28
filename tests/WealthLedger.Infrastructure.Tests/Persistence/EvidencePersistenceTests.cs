using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class EvidencePersistenceTests
{
    [Fact]
    public async Task MetadataOnlyEvidence_RoundTripsAfterRestartWithStableCodes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-metadata",
                EvidenceTestData.Command(
                    label: "Synthetic count",
                    institutionId: CoreLedgerTestData.InstitutionId,
                    accountId: CoreLedgerTestData.AccountId,
                    reference: "SYN-COUNT-1",
                    note: "Synthetic\nnote",
                    transactionIds: [fixture.SecondPostedId, fixture.FirstPostedId]) with
                {
                    SourceKind = EvidenceSourceKind.PhysicalCount
                })).EvidenceId;
        }

        await using var restarted = database.CreateContext();
        var detail = await new GetEvidenceUseCase(new EfCoreEvidenceReadStore(restarted))
            .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, evidenceId));

        Assert.Equal(EvidenceSourceKind.PhysicalCount, detail.SourceKind);
        Assert.Equal(EvidenceTestData.EffectiveDate, detail.EffectiveDate);
        Assert.Equal("Synthetic count", detail.Label);
        Assert.Equal("SYN-COUNT-1", detail.Reference);
        Assert.Equal("Synthetic\nnote", detail.Note);
        Assert.Equal(EvidenceTestData.Now, detail.CreatedAtUtc);
        Assert.Equal(CoreLedgerTestData.InstitutionId, detail.Context.InstitutionId);
        Assert.Equal("TEST_INSTITUTION", detail.Context.InstitutionCode);
        Assert.Equal(CoreLedgerTestData.AccountId, detail.Context.AccountId);
        Assert.Equal("PRIMARY", detail.Context.AccountCode);
        Assert.Null(detail.Attachment);
        Assert.Null(detail.SupersededByEvidenceId);
        Assert.Equal(
            new[] { fixture.FirstPostedId, fixture.SecondPostedId }.Order(),
            detail.TransactionLinks.Select(x => x.TransactionId).Order());
        Assert.All(detail.TransactionLinks, link =>
        {
            Assert.Equal(TransactionType.Contribution, link.TransactionType);
            Assert.Equal(CoreLedgerTestData.ExecutionDate, link.ExecutionDate);
            Assert.Equal(EvidenceTestData.Now, link.LinkedAtUtc);
        });

        Assert.Equal(
            "PHYSICAL_COUNT|2026-09-30",
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT SourceKindCode || '|' || EffectiveDate FROM EvidenceRecord WHERE Id = $id;",
                new SqliteParameter("$id", evidenceId.ToString("D")))));
    }

    [Fact]
    public async Task Attachment_RoundTripsByteForByteWithStableSha256()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-attachment",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        var expectedSha256 = EvidenceAttachment.ComputeSha256(EvidenceTestData.SyntheticPdf);

        Assert.Equal(
            $"PDF|blob|{EvidenceTestData.SyntheticPdf.Length}|{EvidenceTestData.SyntheticPdf.Length}|{expectedSha256}|synthetic-statement.pdf",
            Convert.ToString(await database.ExecuteScalarAsync(
                """
                SELECT MediaTypeCode || '|' || typeof(Content) || '|' || ByteLength
                    || '|' || length(Content) || '|' || Sha256 || '|' || FileName
                FROM EvidenceAttachment
                WHERE EvidenceId = $id;
                """,
                new SqliteParameter("$id", evidenceId.ToString("D")))));

        await using var restarted = database.CreateContext();
        var readStore = new EfCoreEvidenceReadStore(restarted);
        var content = await new GetEvidenceAttachmentContentUseCase(readStore)
            .ExecuteAsync(new GetEvidenceAttachmentContentQuery(
                CoreLedgerTestData.HouseholdId,
                evidenceId));
        var metadata = (await new GetEvidenceUseCase(readStore)
                .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, evidenceId)))
            .Attachment!;

        Assert.Equal(EvidenceTestData.SyntheticPdf, content.Content.ToArray());
        Assert.Equal(expectedSha256, content.Sha256);
        Assert.Equal(expectedSha256, EvidenceAttachment.ComputeSha256(content.Content.Span));
        Assert.Equal(expectedSha256, metadata.Sha256);
        Assert.Equal(EvidenceTestData.SyntheticPdf.Length, metadata.ByteLength);
        Assert.Equal(EvidenceMediaType.Pdf, metadata.MediaType);
        Assert.Equal(EvidenceTestData.Now, metadata.CreatedAtUtc);
    }

    [Fact]
    public async Task Attachment_AcceptsExactlyTenMebibytes()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var content = new byte[EvidenceAttachmentPolicy.MaximumByteLength];
        Array.Fill(content, (byte)'a');

        await using (var context = database.CreateContext())
        {
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-maximum",
                EvidenceTestData.Command(attachment: new EvidenceAttachmentUpload(
                    "maximum.txt",
                    EvidenceMediaType.PlainText,
                    content)));
        }

        Assert.Equal(
            10_485_760L,
            Convert.ToInt64(await database.ExecuteScalarAsync(
                "SELECT length(Content) FROM EvidenceAttachment;")));
    }

    [Fact]
    public async Task Replay_ReturnsOriginalAndRetainsOneCopy()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid first;
        Guid replay;

        await using (var context = database.CreateContext())
        {
            first = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-replay",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        await using (var context = database.CreateContext())
        {
            replay = (await EvidenceTestData.RecordUseCase(context, EvidenceTestData.Now.AddHours(1))
                .ExecuteAsync(
                    "evidence-replay",
                    EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<IdempotencyConflictException>(
                () => EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                    "evidence-replay",
                    EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload(
                        Encoding.ASCII.GetBytes("%PDF-1.7\n%different\n")))));
        }

        Assert.Equal(first, replay);
        Assert.Equal(1L, await CountAsync(database, "EvidenceRecord"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceAttachment"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceCommandReceipt"));
    }

    [Fact]
    public async Task ConcurrentEquivalentRecords_RetainOneCopy()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var start = new SemaphoreSlim(0, 2);

        async Task<Guid> RecordAsync(WealthLedgerDbContext context)
        {
            await start.WaitAsync();
            return (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-race",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        var first = Task.Run(() => RecordAsync(firstContext));
        var second = Task.Run(() => RecordAsync(secondContext));
        start.Release(2);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(results[0], results[1]);
        Assert.Equal(1L, await CountAsync(database, "EvidenceRecord"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceAttachment"));
    }

    [Fact]
    public async Task LostRace_ReturnsTheWinnerEvenWhenSupersessionCollidesFirst()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid original;
        Guid winner;

        await using (var context = database.CreateContext())
        {
            original = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-race-original",
                EvidenceTestData.Command())).EvidenceId;
            winner = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-race-supersede",
                EvidenceTestData.Command(supersedesEvidenceId: original))).EvidenceId;
        }

        // The loser validated before the winner committed, so it reaches the
        // store with the same scope and its own superseding record.
        await using var loserContext = database.CreateContext();
        var loser = EvidenceRecord.Create(
            Guid.NewGuid(),
            CoreLedgerTestData.HouseholdId,
            EvidenceSourceKind.Statement,
            EvidenceTestData.EffectiveDate,
            "Synthetic statement",
            EvidenceTestData.Now.AddMinutes(1),
            supersedesEvidenceId: original);
        var result = await new EfCoreEvidenceStore(loserContext).TryCommitAsync(
            LoserReceipt("evidence-race-supersede", loser),
            loser);

        Assert.False(result.WasCommitted);
        Assert.Equal(winner, result.Receipt.EvidenceId);
        Assert.Equal(2L, await CountAsync(database, "EvidenceRecord"));
    }

    [Fact]
    public async Task LostRace_ReceiptCollisionReturnsTheWinnerAndRollsBackTheLoser()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid winner;

        await using (var context = database.CreateContext())
        {
            winner = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-race-receipt",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        await using var loserContext = database.CreateContext();
        var loser = EvidenceRecord.Create(
            Guid.NewGuid(),
            CoreLedgerTestData.HouseholdId,
            EvidenceSourceKind.Statement,
            EvidenceTestData.EffectiveDate,
            "Synthetic statement",
            EvidenceTestData.Now.AddMinutes(1),
            attachment: EvidenceAttachment.Create(
                "synthetic-statement.pdf",
                EvidenceMediaType.Pdf,
                EvidenceTestData.SyntheticPdf));
        var result = await new EfCoreEvidenceStore(loserContext).TryCommitAsync(
            LoserReceipt("evidence-race-receipt", loser),
            loser);

        Assert.False(result.WasCommitted);
        Assert.Equal(winner, result.Receipt.EvidenceId);
        Assert.Equal(1L, await CountAsync(database, "EvidenceRecord"));
        Assert.Equal(1L, await CountAsync(database, "EvidenceAttachment"));
    }

    [Fact]
    public async Task SupersessionCollisionUnderAnotherKeyIsAConflict()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid original;

        await using (var context = database.CreateContext())
        {
            original = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-conflict-original",
                EvidenceTestData.Command())).EvidenceId;
            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-conflict-first",
                EvidenceTestData.Command(supersedesEvidenceId: original));
        }

        await using var loserContext = database.CreateContext();
        var loser = EvidenceRecord.Create(
            Guid.NewGuid(),
            CoreLedgerTestData.HouseholdId,
            EvidenceSourceKind.Statement,
            EvidenceTestData.EffectiveDate,
            "Synthetic other replacement",
            EvidenceTestData.Now.AddMinutes(1),
            supersedesEvidenceId: original);

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new EfCoreEvidenceStore(loserContext).TryCommitAsync(
                LoserReceipt("evidence-conflict-second", loser),
                loser));

        Assert.Equal(EvidenceErrorCodes.SupersessionInvalid, exception.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.Conflict, exception.Category);
    }

    [Fact]
    public async Task AttachmentContent_FailsClosedForMarkupStoredAsPdf()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var evidenceId = Guid.NewGuid();
        var markup = Encoding.UTF8.GetBytes("<html><script></script></html>");

        // A row written outside the Application with a self-consistent digest.
        await database.ExecuteNonQueryAsync(
            """
            INSERT INTO EvidenceRecord (
                Id, HouseholdId, SourceKindCode, EffectiveDate, Label, CreatedAtUtc)
            VALUES ($id, $household, 'RECEIPT', '2026-09-30', 'Synthetic', '2026-10-01T09:00:00.0000000Z');
            INSERT INTO EvidenceAttachment (
                EvidenceId, FileName, MediaTypeCode, ByteLength, Sha256, Content, CreatedAtUtc)
            VALUES ($id, 'disguised.pdf', 'PDF', $length, $sha, $content, '2026-10-01T09:00:00.0000000Z');
            """,
            new SqliteParameter("$id", evidenceId.ToString("D")),
            new SqliteParameter("$household", CoreLedgerTestData.HouseholdId.ToString("D")),
            new SqliteParameter("$length", markup.Length),
            new SqliteParameter("$sha", EvidenceAttachment.ComputeSha256(markup)),
            new SqliteParameter("$content", markup));

        await using var context = database.CreateContext();
        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new GetEvidenceAttachmentContentUseCase(new EfCoreEvidenceReadStore(context))
                .ExecuteAsync(new GetEvidenceAttachmentContentQuery(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId)));

        Assert.Equal(EvidenceErrorCodes.AttachmentIntegrityFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task Linking_AddsOnlyNewLinksAndLeavesLedgerRowsUntouched()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        var ledgerBefore = await SnapshotLedgerAsync(database);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-link",
                EvidenceTestData.Command(transactionIds: [fixture.FirstPostedId]))).EvidenceId;
        }

        LinkEvidenceTransactionsResult result;

        await using (var context = database.CreateContext())
        {
            result = await EvidenceTestData.LinkUseCase(context, EvidenceTestData.Now.AddHours(2))
                .ExecuteAsync(new LinkEvidenceTransactionsCommand(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId,
                    [fixture.FirstPostedId, fixture.SecondPostedId]));
        }

        await using (var context = database.CreateContext())
        {
            var invalid = await Assert.ThrowsAsync<EvidenceException>(
                () => EvidenceTestData.LinkUseCase(context).ExecuteAsync(
                    new LinkEvidenceTransactionsCommand(
                        CoreLedgerTestData.HouseholdId,
                        evidenceId,
                        [fixture.DraftId, fixture.OtherHouseholdPostedId])));
            Assert.Equal(EvidenceErrorCodes.TransactionLinkInvalid, invalid.ErrorCode);
        }

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.AlreadyLinkedCount);
        Assert.Equal(2L, await CountAsync(database, "EvidenceTransactionLink"));
        Assert.Equal(
            "2026-10-01T11:00:00.0000000Z",
            Convert.ToString(await database.ExecuteScalarAsync(
                "SELECT LinkedAtUtc FROM EvidenceTransactionLink WHERE LedgerTransactionId = $id;",
                new SqliteParameter("$id", fixture.SecondPostedId.ToString("D")))));
        Assert.Equal(ledgerBefore, await SnapshotLedgerAsync(database));
    }

    [Fact]
    public async Task Supersession_PreservesTheOriginalAndFormsOneChain()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid original;
        Guid replacement;

        await using (var context = database.CreateContext())
        {
            original = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-original",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
            replacement = (await EvidenceTestData.RecordUseCase(context, EvidenceTestData.Now.AddHours(1))
                .ExecuteAsync(
                    "evidence-replacement",
                    EvidenceTestData.Command(
                        label: "Synthetic corrected statement",
                        supersedesEvidenceId: original,
                        attachment: new EvidenceAttachmentUpload(
                            "corrected.csv",
                            EvidenceMediaType.Csv,
                            EvidenceTestData.SyntheticCsv)))).EvidenceId;
        }

        await using (var context = database.CreateContext())
        {
            var second = await Assert.ThrowsAsync<EvidenceException>(
                () => EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                    "evidence-second-replacement",
                    EvidenceTestData.Command(supersedesEvidenceId: original)));
            var link = await Assert.ThrowsAsync<EvidenceException>(
                () => EvidenceTestData.LinkUseCase(context).ExecuteAsync(
                    new LinkEvidenceTransactionsCommand(
                        CoreLedgerTestData.HouseholdId,
                        original,
                        [fixture.FirstPostedId])));

            Assert.Equal(EvidenceErrorCodes.SupersessionInvalid, second.ErrorCode);
            Assert.Equal(EvidenceErrorCategory.Conflict, second.Category);
            Assert.Equal(EvidenceErrorCodes.Superseded, link.ErrorCode);
        }

        await using var restarted = database.CreateContext();
        var readStore = new EfCoreEvidenceReadStore(restarted);
        var originalDetail = await new GetEvidenceUseCase(readStore)
            .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, original));
        var originalContent = await new GetEvidenceAttachmentContentUseCase(readStore)
            .ExecuteAsync(new GetEvidenceAttachmentContentQuery(CoreLedgerTestData.HouseholdId, original));
        var replacementDetail = await new GetEvidenceUseCase(readStore)
            .ExecuteAsync(new GetEvidenceQuery(CoreLedgerTestData.HouseholdId, replacement));

        Assert.Equal(replacement, originalDetail.SupersededByEvidenceId);
        Assert.Equal("Synthetic statement", originalDetail.Label);
        Assert.Equal(EvidenceTestData.SyntheticPdf, originalContent.Content.ToArray());
        Assert.Equal(original, replacementDetail.SupersedesEvidenceId);
        Assert.Equal(EvidenceMediaType.Csv, replacementDetail.Attachment!.MediaType);
    }

    [Fact]
    public async Task List_PagesDeterministicallyAcrossTiesWithoutDuplicatesOrGaps()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        var expected = new List<(DateOnly Date, DateTimeOffset CreatedAt, Guid Id)>();

        await using (var context = database.CreateContext())
        {
            // Two dates, two creation times per date, and same-time ties.
            for (var index = 0; index < 7; index++)
            {
                var date = new DateOnly(2026, 9, 20 + (index % 2));
                var createdAt = EvidenceTestData.Now.AddMinutes(index % 3 == 0 ? 0 : 5);
                var id = (await EvidenceTestData.RecordUseCase(context, createdAt).ExecuteAsync(
                    $"evidence-page-{index}",
                    EvidenceTestData.Command(
                        label: $"Synthetic {index}",
                        effectiveDate: date))).EvidenceId;
                expected.Add((date, createdAt, id));
            }

            await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-other-household",
                EvidenceTestData.Command(householdId: CoreLedgerTestData.OtherHouseholdId));
        }

        var ordered = expected
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id.ToString("D"), StringComparer.Ordinal)
            .Select(x => x.Id)
            .ToArray();
        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        await using var restarted = database.CreateContext();
        var useCase = new ListEvidenceUseCase(new EfCoreEvidenceReadStore(restarted));

        do
        {
            var page = await useCase.ExecuteAsync(
                new ListEvidenceQuery(CoreLedgerTestData.HouseholdId, PageSize: 3, Cursor: cursor));
            seen.AddRange(page.Items.Select(x => x.EvidenceId));
            Assert.All(page.Items, item => Assert.Equal(CoreLedgerTestData.HouseholdId, item.HouseholdId));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(ordered, seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task Reads_AreHouseholdScoped()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-scope",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        await using var restarted = database.CreateContext();
        var readStore = new EfCoreEvidenceReadStore(restarted);
        var detail = await Assert.ThrowsAsync<EvidenceException>(
            () => new GetEvidenceUseCase(readStore).ExecuteAsync(
                new GetEvidenceQuery(CoreLedgerTestData.OtherHouseholdId, evidenceId)));
        var content = await Assert.ThrowsAsync<EvidenceException>(
            () => new GetEvidenceAttachmentContentUseCase(readStore).ExecuteAsync(
                new GetEvidenceAttachmentContentQuery(CoreLedgerTestData.OtherHouseholdId, evidenceId)));
        var list = await new ListEvidenceUseCase(readStore).ExecuteAsync(
            new ListEvidenceQuery(CoreLedgerTestData.OtherHouseholdId));

        Assert.Equal(EvidenceErrorCodes.NotFound, detail.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.AttachmentNotFound, content.ErrorCode);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Record_RejectsCrossHouseholdOrMismatchedContextAgainstRealMasters()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        await using var context = database.CreateContext();
        var useCase = EvidenceTestData.RecordUseCase(context);

        var crossHousehold = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                "evidence-cross",
                EvidenceTestData.Command(accountId: CoreLedgerTestData.OtherAccountId)));
        var mismatch = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                "evidence-mismatch",
                EvidenceTestData.Command(
                    institutionId: EvidenceTestData.SecondInstitutionId,
                    accountId: CoreLedgerTestData.AccountId)));

        Assert.Equal(EvidenceErrorCodes.ContextNotFound, crossHousehold.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.ContextMismatch, mismatch.ErrorCode);
        Assert.Equal(0L, await CountAsync(database, "EvidenceRecord"));
    }

    [Fact]
    public async Task AttachmentContent_FailsClosedWhenStoredBytesAreAltered()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await EvidenceTestData.SeedAsync(database.CreateContext);
        Guid evidenceId;

        await using (var context = database.CreateContext())
        {
            evidenceId = (await EvidenceTestData.RecordUseCase(context).ExecuteAsync(
                "evidence-tamper",
                EvidenceTestData.Command(attachment: EvidenceTestData.PdfUpload()))).EvidenceId;
        }

        // Simulates storage corruption: the guard must be removed first,
        // because ordinary SQL cannot rewrite retained bytes.
        var altered = EvidenceTestData.SyntheticPdf.ToArray();
        altered[^2] ^= 0x01;
        await database.ExecuteNonQueryAsync(
            """
            DROP TRIGGER TR_EvidenceAttachment_PreventUpdate;
            UPDATE EvidenceAttachment SET Content = $content WHERE EvidenceId = $id;
            """,
            new SqliteParameter("$content", altered),
            new SqliteParameter("$id", evidenceId.ToString("D")));

        await using var restarted = database.CreateContext();
        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new GetEvidenceAttachmentContentUseCase(new EfCoreEvidenceReadStore(restarted))
                .ExecuteAsync(new GetEvidenceAttachmentContentQuery(
                    CoreLedgerTestData.HouseholdId,
                    evidenceId)));

        Assert.Equal(EvidenceErrorCodes.AttachmentIntegrityFailed, exception.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.Integrity, exception.Category);
    }

    private static EvidenceCommandReceipt LoserReceipt(
        string idempotencyKey,
        EvidenceRecord record)
        => new(
            new EvidenceCommandScope(
                record.HouseholdId,
                EvidenceOperationCodes.RecordEvidence,
                idempotencyKey),
            new CommandFingerprint("SHA256", 1, new string('0', 64)),
            record.Id,
            record.CreatedAtUtc);

    private static async Task<long> CountAsync(SqliteTestDatabase database, string table)
        => Convert.ToInt64(await database.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM \"{table}\";"));

    private static async Task<string> SnapshotLedgerAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        var rows = await context.LedgerTransactions
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.PostedAtUtc, x.ExternalReference, x.Note })
            .ToListAsync();
        var entries = await context.TransactionEntries
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityDeltaE8 })
            .ToListAsync();

        return string.Join(';', rows) + "#" + string.Join(';', entries);
    }
}
