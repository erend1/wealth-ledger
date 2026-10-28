using System.Text;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.Tests.Evidence;

public sealed class EvidenceUseCaseTests
{
    private static readonly Guid HouseholdId = Id("a1");
    private static readonly Guid OtherHouseholdId = Id("a2");
    private static readonly Guid InstitutionId = Id("b1");
    private static readonly Guid OtherInstitutionId = Id("b2");
    private static readonly Guid AccountId = Id("c1");
    private static readonly Guid AccountWithoutInstitutionId = Id("c2");
    private static readonly Guid OtherHouseholdAccountId = Id("c3");
    private static readonly Guid PostedTransactionId = Id("d1");
    private static readonly Guid SecondPostedTransactionId = Id("d2");
    private static readonly Guid DraftTransactionId = Id("d3");
    private static readonly Guid OtherHouseholdTransactionId = Id("d4");
    private static readonly Guid ExistingEvidenceId = Id("e1");
    private static readonly Guid SupersededEvidenceId = Id("e2");
    private static readonly Guid OtherHouseholdEvidenceId = Id("e3");

    // 21:30 UTC is already the next day at +03:00.
    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 21, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly LocalToday = new(2026, 10, 2);

    private static readonly byte[] SyntheticPdf =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%synthetic\n%%EOF\n");

    [Fact]
    public async Task Record_MetadataOnlyCommitsNormalizedRecordAndReceipt()
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateRecordUseCase(store);

        var result = await useCase.ExecuteAsync(
            "key-1",
            Command(
                label: "  Synthetic statement  ",
                reference: "  SYN-1  ",
                institutionId: InstitutionId,
                accountId: AccountId));

        var record = Assert.Single(store.CommittedRecords);
        Assert.Equal(result.EvidenceId, record.Id);
        Assert.Equal(HouseholdId, record.HouseholdId);
        Assert.Equal("Synthetic statement", record.Label);
        Assert.Equal("SYN-1", record.Reference);
        Assert.Equal(InstitutionId, record.InstitutionId);
        Assert.Equal(AccountId, record.AccountId);
        Assert.Equal(Now, record.CreatedAtUtc);
        Assert.Null(record.Attachment);
        Assert.Empty(record.TransactionLinks);

        var receipt = Assert.Single(store.Receipts.Values);
        Assert.Equal(EvidenceOperationCodes.RecordEvidence, receipt.Scope.OperationCode);
        Assert.Equal("key-1", receipt.Scope.IdempotencyKey);
        Assert.Equal("SHA256", receipt.Fingerprint.AlgorithmCode);
        Assert.Equal(1, receipt.Fingerprint.Version);
        Assert.Equal(64, receipt.Fingerprint.Value.Length);
        Assert.Equal(record.Id, receipt.EvidenceId);
    }

    [Fact]
    public async Task Record_RetainsVerifiedAttachmentAndSortedLinks()
    {
        var store = new FakeEvidenceStore();

        await CreateRecordUseCase(store).ExecuteAsync(
            "key-1",
            Command(
                transactionIds: [SecondPostedTransactionId, PostedTransactionId, SecondPostedTransactionId],
                attachment: new EvidenceAttachmentUpload(
                    @"C:\synthetic\statement.pdf",
                    EvidenceMediaType.Pdf,
                    SyntheticPdf)));

        var record = Assert.Single(store.CommittedRecords);
        var attachment = Assert.IsType<EvidenceAttachment>(record.Attachment);
        Assert.Equal("statement.pdf", attachment.FileName);
        Assert.Equal(SyntheticPdf.Length, attachment.ByteLength);
        Assert.Equal(EvidenceAttachment.ComputeSha256(SyntheticPdf), attachment.Sha256);
        Assert.Equal(
            [PostedTransactionId, SecondPostedTransactionId],
            record.TransactionLinks.Select(x => x.TransactionId));
    }

    [Fact]
    public async Task Record_EquivalentReplayReturnsOriginalWithoutRetainingAgain()
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateRecordUseCase(store);
        var upload = new EvidenceAttachmentUpload(
            "statement.pdf",
            EvidenceMediaType.Pdf,
            SyntheticPdf);

        var first = await useCase.ExecuteAsync(
            "key-1",
            Command(
                label: "Synthetic statement",
                transactionIds: [PostedTransactionId, SecondPostedTransactionId],
                attachment: upload));
        var replay = await useCase.ExecuteAsync(
            "key-1",
            Command(
                label: "  Synthetic statement ",
                transactionIds: [SecondPostedTransactionId, PostedTransactionId, PostedTransactionId],
                attachment: new EvidenceAttachmentUpload(
                    "/synthetic/statement.pdf",
                    EvidenceMediaType.Pdf,
                    SyntheticPdf.ToArray())));

        Assert.Equal(first.EvidenceId, replay.EvidenceId);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task Record_SameKeyWithDifferentBytesIsAConflict()
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateRecordUseCase(store);

        await useCase.ExecuteAsync(
            "key-1",
            Command(attachment: new EvidenceAttachmentUpload(
                "statement.pdf",
                EvidenceMediaType.Pdf,
                SyntheticPdf)));

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => useCase.ExecuteAsync(
                "key-1",
                Command(attachment: new EvidenceAttachmentUpload(
                    "statement.pdf",
                    EvidenceMediaType.Pdf,
                    Encoding.ASCII.GetBytes("%PDF-1.7\n%other\n")))));
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task Record_LostRaceReturnsTheWinningReceipt()
    {
        var store = new FakeEvidenceStore();
        var winnerId = Guid.NewGuid();
        store.OnCommit = (receipt, _) => receipt with { EvidenceId = winnerId };

        var result = await CreateRecordUseCase(store).ExecuteAsync(
            "key-1",
            Command());

        Assert.Equal(winnerId, result.EvidenceId);
        Assert.Empty(store.CommittedRecords);
    }

    [Fact]
    public async Task Record_LostRaceToADifferentCommandIsAConflict()
    {
        var store = new FakeEvidenceStore();
        store.OnCommit = (receipt, _) => receipt with
        {
            EvidenceId = Guid.NewGuid(),
            Fingerprint = receipt.Fingerprint with { Value = new string('0', 64) }
        };

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => CreateRecordUseCase(store).ExecuteAsync("key-1", Command()));
    }

    [Fact]
    public async Task Record_EffectiveDateUsesTheOperatingLocalDate()
    {
        var store = new FakeEvidenceStore();
        var useCase = CreateRecordUseCase(store);

        await useCase.ExecuteAsync("key-1", Command(effectiveDate: LocalToday));

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                "key-2",
                Command(effectiveDate: LocalToday.AddDays(1))));

        Assert.Equal(EvidenceErrorCodes.EffectiveDateInFuture, exception.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.Validation, exception.Category);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task Record_UnknownHouseholdIsNotFound()
    {
        var exception = await RecordFailsAsync(Command(householdId: Id("ff")));

        Assert.Equal(EvidenceErrorCodes.HouseholdNotFound, exception.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.NotFound, exception.Category);
    }

    [Theory]
    [InlineData("b9", null)]
    [InlineData(null, "c9")]
    [InlineData(null, "c3")]
    public async Task Record_UnknownOrCrossHouseholdContextIsNotFound(
        string? institutionSuffix,
        string? accountSuffix)
    {
        var exception = await RecordFailsAsync(
            Command(
                institutionId: institutionSuffix is null ? null : Id(institutionSuffix),
                accountId: accountSuffix is null ? null : Id(accountSuffix)));

        Assert.Equal(EvidenceErrorCodes.ContextNotFound, exception.ErrorCode);
    }

    [Theory]
    [InlineData("b2", "c1")]
    [InlineData("b1", "c2")]
    public async Task Record_AccountOutsideTheInstitutionIsAMismatch(
        string institutionSuffix,
        string accountSuffix)
    {
        var exception = await RecordFailsAsync(
            Command(
                institutionId: Id(institutionSuffix),
                accountId: Id(accountSuffix)));

        Assert.Equal(EvidenceErrorCodes.ContextMismatch, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_AccountWithoutInstitutionNeedsNoInstitution()
    {
        var store = new FakeEvidenceStore();

        await CreateRecordUseCase(store).ExecuteAsync(
            "key-1",
            Command(accountId: AccountWithoutInstitutionId));

        Assert.Equal(AccountWithoutInstitutionId, store.CommittedRecords.Single().AccountId);
    }

    [Theory]
    [InlineData("d3")]
    [InlineData("d4")]
    [InlineData("d9")]
    public async Task Record_LinksOnlyPostedSameHouseholdTransactions(string transactionSuffix)
    {
        var exception = await RecordFailsAsync(
            Command(transactionIds: [PostedTransactionId, Id(transactionSuffix)]));

        Assert.Equal(EvidenceErrorCodes.TransactionLinkInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_RejectsEmptyAndTooManyLinks()
    {
        var empty = await RecordFailsAsync(Command(transactionIds: [Guid.Empty]));
        var tooMany = await RecordFailsAsync(
            Command(transactionIds: Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()));

        Assert.Equal(EvidenceErrorCodes.TransactionLinkInvalid, empty.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.TransactionLinkLimitExceeded, tooMany.ErrorCode);
    }

    [Fact]
    public async Task Record_SupersedesOnlyTheLatestSameHouseholdRecord()
    {
        var unknown = await RecordFailsAsync(Command(supersedesEvidenceId: Id("e9")));
        var crossHousehold = await RecordFailsAsync(
            Command(supersedesEvidenceId: OtherHouseholdEvidenceId));
        var alreadySuperseded = await RecordFailsAsync(
            Command(supersedesEvidenceId: SupersededEvidenceId));

        Assert.Equal(EvidenceErrorCodes.SupersessionInvalid, unknown.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.SupersessionInvalid, crossHousehold.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.SupersessionInvalid, alreadySuperseded.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.Conflict, alreadySuperseded.Category);

        var store = new FakeEvidenceStore();
        await CreateRecordUseCase(store).ExecuteAsync(
            "key-1",
            Command(supersedesEvidenceId: ExistingEvidenceId));
        Assert.Equal(ExistingEvidenceId, store.CommittedRecords.Single().SupersedesEvidenceId);
    }

    public static TheoryData<string, EvidenceMediaType, byte[], string> RejectedUploads()
        => new()
        {
            { "empty.pdf", EvidenceMediaType.Pdf, [], EvidenceErrorCodes.AttachmentEmpty },
            { "large.txt", EvidenceMediaType.PlainText, new byte[EvidenceAttachmentPolicy.MaximumByteLength + 1], EvidenceErrorCodes.AttachmentTooLarge },
            { "...", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceErrorCodes.AttachmentFileNameInvalid },
            { "image.svg", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("<svg/>"), EvidenceErrorCodes.AttachmentTypeMismatch },
            { "image.txt", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("<svg/>"), EvidenceErrorCodes.AttachmentContentRejected },
            { "statement.pdf", (EvidenceMediaType)42, SyntheticPdf, EvidenceErrorCodes.AttachmentTypeMismatch }
        };

    [Theory]
    [MemberData(nameof(RejectedUploads), DisableDiscoveryEnumeration = true)]
    public async Task Record_RejectedAttachmentFailsBeforeAnyStoreAccess(
        string fileName,
        EvidenceMediaType mediaType,
        byte[] content,
        string expectedCode)
    {
        var store = new FakeEvidenceStore();

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateRecordUseCase(store).ExecuteAsync(
                "key-1",
                Command(attachment: new EvidenceAttachmentUpload(
                    fileName,
                    mediaType,
                    content))));

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(EvidenceErrorCategory.Validation, exception.Category);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two\nlines")]
    public async Task Record_InvalidLabelIsRejected(string label)
    {
        var exception = await RecordFailsAsync(Command(label: label));

        Assert.Equal(EvidenceErrorCodes.InputInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_UndefinedSourceKindIsRejected()
    {
        var exception = await RecordFailsAsync(
            Command() with { SourceKind = (EvidenceSourceKind)42 });

        Assert.Equal(EvidenceErrorCodes.InputInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Record_FailuresAndCommandTextNeverEchoSensitiveInput()
    {
        const string Marker = "SYNTHETIC-SECRET";
        var commands = new[]
        {
            Command(label: Marker + "\nlabel"),
            Command(label: Marker, reference: Marker + "\u0007"),
            Command(label: Marker, note: Marker + "\u0000"),
            Command(label: Marker, attachment: new EvidenceAttachmentUpload(
                Marker + ".svg",
                EvidenceMediaType.PlainText,
                Encoding.UTF8.GetBytes(Marker))),
            Command(label: Marker, attachment: new EvidenceAttachmentUpload(
                Marker + ".txt",
                EvidenceMediaType.PlainText,
                Encoding.UTF8.GetBytes("<" + Marker)))
        };

        foreach (var command in commands)
        {
            var exception = await RecordFailsAsync(command);

            Assert.DoesNotContain(Marker, exception.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, command.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, command.Attachment?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Link_AddsOnlyNewLinksAndReportsExistingOnes()
    {
        var store = new FakeEvidenceStore();
        store.ExistingLinks.Add((ExistingEvidenceId, PostedTransactionId));

        var result = await new LinkEvidenceTransactionsUseCase(store, new FixedTimeProvider())
            .ExecuteAsync(new LinkEvidenceTransactionsCommand(
                HouseholdId,
                ExistingEvidenceId,
                [SecondPostedTransactionId, PostedTransactionId, SecondPostedTransactionId]));

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.AlreadyLinkedCount);
        Assert.Contains((ExistingEvidenceId, SecondPostedTransactionId), store.ExistingLinks);
        Assert.All(store.AddedLinks, link => Assert.Equal(Now, link.LinkedAtUtc));
    }

    [Theory]
    [InlineData("e2", "a1", EvidenceErrorCodes.Superseded)]
    [InlineData("e3", "a1", EvidenceErrorCodes.NotFound)]
    [InlineData("e9", "a1", EvidenceErrorCodes.NotFound)]
    [InlineData("e1", "a2", EvidenceErrorCodes.NotFound)]
    public async Task Link_RefusesSupersededOrForeignEvidence(
        string evidenceSuffix,
        string householdSuffix,
        string expectedCode)
    {
        var store = new FakeEvidenceStore();

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new LinkEvidenceTransactionsUseCase(store, new FixedTimeProvider())
                .ExecuteAsync(new LinkEvidenceTransactionsCommand(
                    Id(householdSuffix),
                    Id(evidenceSuffix),
                    [PostedTransactionId])));

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Empty(store.AddedLinks);
    }

    [Fact]
    public async Task Link_RequiresAtLeastOnePostedSameHouseholdTransaction()
    {
        var store = new FakeEvidenceStore();
        var useCase = new LinkEvidenceTransactionsUseCase(store, new FixedTimeProvider());

        var empty = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(new LinkEvidenceTransactionsCommand(
                HouseholdId,
                ExistingEvidenceId,
                [])));
        var draft = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(new LinkEvidenceTransactionsCommand(
                HouseholdId,
                ExistingEvidenceId,
                [DraftTransactionId])));

        Assert.Equal(EvidenceErrorCodes.TransactionLinkInvalid, empty.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.TransactionLinkInvalid, draft.ErrorCode);
        Assert.Empty(store.AddedLinks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_RejectsPageSizeOutsideBounds(int pageSize)
    {
        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new ListEvidenceUseCase(new FakeEvidenceReadStore())
                .ExecuteAsync(new ListEvidenceQuery(HouseholdId, pageSize)));

        Assert.Equal(EvidenceErrorCodes.PageSizeInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task List_UnknownHouseholdIsNotFound()
    {
        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new ListEvidenceUseCase(new FakeEvidenceReadStore())
                .ExecuteAsync(new ListEvidenceQuery(Id("ff"))));

        Assert.Equal(EvidenceErrorCodes.HouseholdNotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task List_PagesWithAHouseholdBoundCursor()
    {
        var readStore = new FakeEvidenceReadStore();
        readStore.Summaries.AddRange(
            Enumerable.Range(1, 3).Select(day => Summary(new DateOnly(2026, 9, day))));
        var useCase = new ListEvidenceUseCase(readStore);

        var first = await useCase.ExecuteAsync(new ListEvidenceQuery(HouseholdId, PageSize: 2));
        var second = await useCase.ExecuteAsync(
            new ListEvidenceQuery(HouseholdId, PageSize: 2, Cursor: first.NextCursor));

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(3, readStore.LastTake);
        Assert.Equal(first.Items[^1].EvidenceId, readStore.LastAfter!.EvidenceId);
        Assert.Equal(first.Items[^1].EffectiveDate, readStore.LastAfter.EffectiveDate);
        Assert.Equal(first.Items[^1].CreatedAtUtc, readStore.LastAfter.CreatedAtUtc);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);

        var foreign = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                new ListEvidenceQuery(OtherHouseholdId, Cursor: first.NextCursor)));
        var malformed = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(
                new ListEvidenceQuery(HouseholdId, Cursor: "not a cursor")));

        Assert.Equal(EvidenceErrorCodes.CursorScopeMismatch, foreign.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.CursorInvalid, malformed.ErrorCode);
    }

    [Fact]
    public async Task Get_ReturnsOnlySameHouseholdEvidence()
    {
        var readStore = new FakeEvidenceReadStore();
        var detail = Detail(ExistingEvidenceId);
        readStore.Details.Add(detail);
        var useCase = new GetEvidenceUseCase(readStore);

        Assert.Same(
            detail,
            await useCase.ExecuteAsync(new GetEvidenceQuery(HouseholdId, ExistingEvidenceId)));

        var foreign = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(new GetEvidenceQuery(OtherHouseholdId, ExistingEvidenceId)));
        var missing = await Assert.ThrowsAsync<EvidenceException>(
            () => useCase.ExecuteAsync(new GetEvidenceQuery(HouseholdId, Id("e9"))));

        Assert.Equal(EvidenceErrorCodes.NotFound, foreign.ErrorCode);
        Assert.Equal(EvidenceErrorCodes.NotFound, missing.ErrorCode);
    }

    [Fact]
    public async Task AttachmentContent_ReturnsVerifiedBytes()
    {
        var readStore = new FakeEvidenceReadStore();
        readStore.Attachments.Add(Stored(SyntheticPdf, SyntheticPdf.Length, EvidenceAttachment.ComputeSha256(SyntheticPdf)));

        var content = await new GetEvidenceAttachmentContentUseCase(readStore)
            .ExecuteAsync(new GetEvidenceAttachmentContentQuery(HouseholdId, ExistingEvidenceId));

        Assert.Equal(SyntheticPdf, content.Content.ToArray());
        Assert.Equal(SyntheticPdf.Length, content.ByteLength);
        Assert.Equal("statement.pdf", content.FileName);
        Assert.Equal(EvidenceMediaType.Pdf, content.MediaType);
        Assert.Equal(nameof(EvidenceAttachmentContent), content.ToString());
    }

    [Fact]
    public async Task AttachmentContent_FailsClosedWhenBytesNoLongerMatch()
    {
        var digest = EvidenceAttachment.ComputeSha256(SyntheticPdf);
        var tampered = SyntheticPdf.ToArray();
        tampered[^2] ^= 0x01;

        foreach (var stored in new[]
                 {
                     Stored(tampered, tampered.Length, digest),
                     Stored(SyntheticPdf, SyntheticPdf.Length + 1, digest),
                     Stored(SyntheticPdf, SyntheticPdf.Length, digest.ToUpperInvariant())
                 })
        {
            var readStore = new FakeEvidenceReadStore();
            readStore.Attachments.Add(stored);

            var exception = await Assert.ThrowsAsync<EvidenceException>(
                () => new GetEvidenceAttachmentContentUseCase(readStore)
                    .ExecuteAsync(new GetEvidenceAttachmentContentQuery(HouseholdId, ExistingEvidenceId)));

            Assert.Equal(EvidenceErrorCodes.AttachmentIntegrityFailed, exception.ErrorCode);
            Assert.Equal(EvidenceErrorCategory.Integrity, exception.Category);
        }
    }

    [Fact]
    public async Task AttachmentContent_MissingAttachmentIsNotFound()
    {
        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => new GetEvidenceAttachmentContentUseCase(new FakeEvidenceReadStore())
                .ExecuteAsync(new GetEvidenceAttachmentContentQuery(HouseholdId, ExistingEvidenceId)));

        Assert.Equal(EvidenceErrorCodes.AttachmentNotFound, exception.ErrorCode);
    }

    private static async Task<EvidenceException> RecordFailsAsync(RecordEvidenceCommand command)
    {
        var store = new FakeEvidenceStore();

        var exception = await Assert.ThrowsAsync<EvidenceException>(
            () => CreateRecordUseCase(store).ExecuteAsync("key-1", command));

        Assert.Equal(0, store.CommitCount);
        return exception;
    }

    private static RecordEvidenceUseCase CreateRecordUseCase(FakeEvidenceStore store)
        => new(store, new FixedTimeProvider());

    private static RecordEvidenceCommand Command(
        Guid? householdId = null,
        DateOnly? effectiveDate = null,
        string label = "Synthetic statement",
        Guid? institutionId = null,
        Guid? accountId = null,
        string? reference = null,
        string? note = null,
        Guid? supersedesEvidenceId = null,
        IReadOnlyList<Guid>? transactionIds = null,
        EvidenceAttachmentUpload? attachment = null)
        => new(
            householdId ?? HouseholdId,
            EvidenceSourceKind.Statement,
            effectiveDate ?? new DateOnly(2026, 9, 30),
            label,
            institutionId,
            accountId,
            reference,
            note,
            supersedesEvidenceId,
            transactionIds,
            attachment);

    private static EvidenceSummary Summary(DateOnly effectiveDate)
        => new(
            Guid.NewGuid(),
            HouseholdId,
            EvidenceSourceKind.Statement,
            effectiveDate,
            "Synthetic",
            null,
            EmptyContext,
            Now,
            null,
            null,
            null,
            0);

    private static EvidenceDetail Detail(Guid evidenceId)
        => new(
            evidenceId,
            HouseholdId,
            EvidenceSourceKind.Receipt,
            new DateOnly(2026, 9, 1),
            "Synthetic receipt",
            null,
            null,
            EmptyContext,
            Now,
            null,
            null,
            null,
            []);

    private static StoredEvidenceAttachment Stored(byte[] content, long byteLength, string sha256)
        => new(
            ExistingEvidenceId,
            "statement.pdf",
            EvidenceMediaType.Pdf,
            byteLength,
            sha256,
            content);

    private static readonly EvidenceContext EmptyContext =
        new(null, null, null, null, null, null);

    private static Guid Id(string suffix)
        => Guid.Parse($"e1000000-0000-0000-0000-0000000000{suffix}");

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly TimeZoneInfo OperatingZone =
            TimeZoneInfo.CreateCustomTimeZone(
                "Synthetic+03",
                TimeSpan.FromHours(3),
                "Synthetic +03",
                "Synthetic +03");

        public override TimeZoneInfo LocalTimeZone => OperatingZone;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeEvidenceStore : IEvidenceStore
    {
        public Dictionary<EvidenceCommandScope, EvidenceCommandReceipt> Receipts { get; } = [];

        public List<EvidenceRecord> CommittedRecords { get; } = [];

        public HashSet<(Guid EvidenceId, Guid TransactionId)> ExistingLinks { get; } = [];

        public List<EvidenceTransactionLink> AddedLinks { get; } = [];

        public Func<EvidenceCommandReceipt, EvidenceRecord, EvidenceCommandReceipt>? OnCommit { get; set; }

        public int CommitCount { get; private set; }

        public int CallCount { get; private set; }

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(householdId == HouseholdId || householdId == OtherHouseholdId);
        }

        public Task<bool> InstitutionExistsAsync(Guid institutionId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(institutionId == InstitutionId || institutionId == OtherInstitutionId);
        }

        public Task<EvidenceAccountReference?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            EvidenceAccountReference? account = accountId switch
            {
                _ when accountId == AccountId => new(AccountId, HouseholdId, InstitutionId),
                _ when accountId == AccountWithoutInstitutionId => new(AccountWithoutInstitutionId, HouseholdId, null),
                _ when accountId == OtherHouseholdAccountId => new(OtherHouseholdAccountId, OtherHouseholdId, InstitutionId),
                _ => null
            };
            return Task.FromResult(account);
        }

        public Task<IReadOnlyList<EvidenceTransactionReference>> FindTransactionsAsync(
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var known = new[]
            {
                new EvidenceTransactionReference(PostedTransactionId, HouseholdId, TransactionStatus.Posted),
                new EvidenceTransactionReference(SecondPostedTransactionId, HouseholdId, TransactionStatus.Posted),
                new EvidenceTransactionReference(DraftTransactionId, HouseholdId, TransactionStatus.Draft),
                new EvidenceTransactionReference(OtherHouseholdTransactionId, OtherHouseholdId, TransactionStatus.Posted)
            };
            IReadOnlyList<EvidenceTransactionReference> found =
                known.Where(x => transactionIds.Contains(x.TransactionId)).ToArray();
            return Task.FromResult(found);
        }

        public Task<EvidenceRecordReference?> FindEvidenceAsync(Guid evidenceId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            EvidenceRecordReference? evidence = evidenceId switch
            {
                _ when evidenceId == ExistingEvidenceId => new(ExistingEvidenceId, HouseholdId, null),
                _ when evidenceId == SupersededEvidenceId => new(SupersededEvidenceId, HouseholdId, ExistingEvidenceId),
                _ when evidenceId == OtherHouseholdEvidenceId => new(OtherHouseholdEvidenceId, OtherHouseholdId, null),
                _ => null
            };
            return Task.FromResult(evidence);
        }

        public Task<EvidenceCommandReceipt?> FindReceiptAsync(
            EvidenceCommandScope scope,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Receipts.GetValueOrDefault(scope));
        }

        public Task<EvidenceCommitResult> TryCommitAsync(
            EvidenceCommandReceipt receipt,
            EvidenceRecord record,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (OnCommit is not null)
            {
                return Task.FromResult(new EvidenceCommitResult(false, OnCommit(receipt, record)));
            }

            CommitCount++;
            CommittedRecords.Add(record);
            Receipts[receipt.Scope] = receipt;
            return Task.FromResult(new EvidenceCommitResult(true, receipt));
        }

        public Task<int> AddTransactionLinksAsync(
            IReadOnlyCollection<EvidenceTransactionLink> links,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var added = 0;

            foreach (var link in links)
            {
                if (ExistingLinks.Add((link.EvidenceId, link.TransactionId)))
                {
                    AddedLinks.Add(link);
                    added++;
                }
            }

            return Task.FromResult(added);
        }
    }

    private sealed class FakeEvidenceReadStore : IEvidenceReadStore
    {
        public List<EvidenceSummary> Summaries { get; } = [];

        public List<EvidenceDetail> Details { get; } = [];

        public List<StoredEvidenceAttachment> Attachments { get; } = [];

        public int LastTake { get; private set; }

        public EvidenceCursorKey? LastAfter { get; private set; }

        public Task<bool> HouseholdExistsAsync(Guid householdId, CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId || householdId == OtherHouseholdId);

        public Task<IReadOnlyList<EvidenceSummary>> ListAsync(
            Guid householdId,
            int take,
            EvidenceCursorKey? after,
            CancellationToken cancellationToken = default)
        {
            LastTake = take;
            LastAfter = after;
            IReadOnlyList<EvidenceSummary> page = Summaries
                .Where(x => x.HouseholdId == householdId)
                .OrderByDescending(x => x.EffectiveDate)
                .Where(x => after is null || x.EffectiveDate < after.EffectiveDate)
                .Take(take)
                .ToArray();
            return Task.FromResult(page);
        }

        public Task<EvidenceDetail?> GetAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Details.SingleOrDefault(
                x => x.HouseholdId == householdId && x.EvidenceId == evidenceId));

        public Task<StoredEvidenceAttachment?> ReadAttachmentAsync(
            Guid householdId,
            Guid evidenceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(householdId == HouseholdId
                ? Attachments.SingleOrDefault(x => x.EvidenceId == evidenceId)
                : null);
    }
}
