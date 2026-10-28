using System.Text;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

internal sealed record EvidenceFixture(
    Guid FirstPostedId,
    Guid SecondPostedId,
    Guid OtherHouseholdPostedId,
    Guid DraftId);

/// <summary>
/// Synthetic evidence fixtures over real posted history.
/// </summary>
internal static class EvidenceTestData
{
    internal static readonly Guid SecondInstitutionId =
        Guid.Parse("30000000-0000-0000-0000-0000000000e1");

    internal static readonly DateTimeOffset Now =
        new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    internal static readonly DateOnly EffectiveDate = new(2026, 9, 30);

    internal static readonly byte[] SyntheticPdf =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%synthetic evidence statement\n%%EOF\n");

    internal static readonly byte[] SyntheticCsv =
        Encoding.UTF8.GetBytes("asset,quantity\nsynthetic,1\n");

    internal static async Task<EvidenceFixture> SeedAsync(
        Func<WealthLedgerDbContext> createContext)
    {
        await using (var context = createContext())
        {
            await CoreLedgerTestData.SeedMasterDataAsync(context);
            context.Institutions.Add(new InstitutionRow
            {
                Id = SecondInstitutionId,
                Code = "SECOND_INSTITUTION",
                Name = "Second Test Institution",
                Type = InstitutionType.Bank,
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        var first = await PostContributionAsync(
            createContext,
            "evidence-first",
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            10_000);
        var second = await PostContributionAsync(
            createContext,
            "evidence-second",
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            CoreLedgerTestData.AccountId,
            20_000);
        var other = await PostContributionAsync(
            createContext,
            "evidence-other",
            CoreLedgerTestData.OtherHouseholdId,
            CoreLedgerTestData.OtherPortfolioId,
            CoreLedgerTestData.OtherAccountId,
            30_000);
        var draftId = Guid.Parse("70000000-0000-0000-0000-0000000000e1");

        await using (var context = createContext())
        {
            context.LedgerTransactions.Add(
                CoreLedgerTestData.CreateDraftTransaction(
                    draftId,
                    TransactionType.Contribution));
            await context.SaveChangesAsync();
        }

        return new EvidenceFixture(first, second, other, draftId);
    }

    internal static RecordEvidenceUseCase RecordUseCase(
        WealthLedgerDbContext context,
        DateTimeOffset? now = null)
        => new(
            new EfCoreEvidenceStore(context),
            new FixedTimeProvider(now ?? Now));

    internal static LinkEvidenceTransactionsUseCase LinkUseCase(
        WealthLedgerDbContext context,
        DateTimeOffset? now = null)
        => new(
            new EfCoreEvidenceStore(context),
            new FixedTimeProvider(now ?? Now));

    internal static RecordEvidenceObservationsUseCase ObservationUseCase(
        WealthLedgerDbContext context,
        DateTimeOffset? now = null)
        => new(
            new EfCoreEvidenceStore(context),
            new FixedTimeProvider(now ?? Now));

    internal static ListEvidenceObservationsUseCase ListObservationsUseCase(
        WealthLedgerDbContext context)
        => new(new EfCoreEvidenceReadStore(context));

    internal static RecordEvidenceCommand Command(
        string label = "Synthetic statement",
        Guid? householdId = null,
        DateOnly? effectiveDate = null,
        Guid? institutionId = null,
        Guid? accountId = null,
        string? reference = null,
        string? note = null,
        Guid? supersedesEvidenceId = null,
        IReadOnlyList<Guid>? transactionIds = null,
        EvidenceAttachmentUpload? attachment = null)
        => new(
            householdId ?? CoreLedgerTestData.HouseholdId,
            Domain.Evidence.EvidenceSourceKind.Statement,
            effectiveDate ?? EffectiveDate,
            label,
            institutionId,
            accountId,
            reference,
            note,
            supersedesEvidenceId,
            transactionIds,
            attachment);

    internal static EvidenceAttachmentUpload PdfUpload(byte[]? content = null)
        => new(
            "synthetic-statement.pdf",
            Domain.Evidence.EvidenceMediaType.Pdf,
            content ?? SyntheticPdf);

    internal static async Task<Guid> PostContributionAsync(
        Func<WealthLedgerDbContext> createContext,
        string idempotencyKey,
        Guid householdId,
        Guid portfolioId,
        Guid accountId,
        long minorUnits)
    {
        await using var context = createContext();
        var result = await new RecordContributionUseCase(
                new EfCoreLedgerReferenceData(context),
                new EfCoreLedgerPostingStore(context),
                new FixedTimeProvider(Now.AddDays(-1)))
            .ExecuteAsync(
                idempotencyKey,
                new RecordContributionCommand(
                    householdId,
                    portfolioId,
                    accountId,
                    CoreLedgerTestData.CashAssetId,
                    Money.FromMinorUnits(minorUnits, CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    CoreLedgerTestData.ExecutionDate));

        return result.TransactionId;
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        internal FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
