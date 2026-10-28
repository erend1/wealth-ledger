using Microsoft.EntityFrameworkCore;
using WealthLedger.Infrastructure.Persistence.Mapping;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Persistence;

public sealed class WealthLedgerDbContext : DbContext
{
    public WealthLedgerDbContext(
        DbContextOptions<WealthLedgerDbContext> options)
        : base(options)
    {
    }

    internal DbSet<CurrencyRow> Currencies => Set<CurrencyRow>();

    internal DbSet<HouseholdRow> Households => Set<HouseholdRow>();

    internal DbSet<HouseholdMemberRow> HouseholdMembers => Set<HouseholdMemberRow>();

    internal DbSet<InstitutionRow> Institutions => Set<InstitutionRow>();

    internal DbSet<PortfolioRow> Portfolios => Set<PortfolioRow>();

    internal DbSet<AccountRow> Accounts => Set<AccountRow>();

    internal DbSet<AssetRow> Assets => Set<AssetRow>();

    internal DbSet<LedgerTransactionRow> LedgerTransactions => Set<LedgerTransactionRow>();

    internal DbSet<TransactionEntryRow> TransactionEntries => Set<TransactionEntryRow>();

    internal DbSet<CashFlowDetailRow> CashFlowDetails => Set<CashFlowDetailRow>();

    internal DbSet<TransactionCostComponentRow> TransactionCostComponents
        => Set<TransactionCostComponentRow>();

    internal DbSet<AssetLotRow> AssetLots => Set<AssetLotRow>();

    internal DbSet<LotEntryAllocationRow> LotEntryAllocations => Set<LotEntryAllocationRow>();

    internal DbSet<PhysicalGoldLotDetailRow> PhysicalGoldLotDetails
        => Set<PhysicalGoldLotDetailRow>();

    internal DbSet<PhysicalGoldLotAllocationDetailRow>
        PhysicalGoldLotAllocationDetails
        => Set<PhysicalGoldLotAllocationDetailRow>();

    internal DbSet<PhysicalGoldTradeDetailRow> PhysicalGoldTradeDetails
        => Set<PhysicalGoldTradeDetailRow>();

    internal DbSet<CommandReceiptRow> CommandReceipts
        => Set<CommandReceiptRow>();

    internal DbSet<EvidenceRecordRow> EvidenceRecords
        => Set<EvidenceRecordRow>();

    internal DbSet<EvidenceAttachmentRow> EvidenceAttachments
        => Set<EvidenceAttachmentRow>();

    internal DbSet<EvidenceTransactionLinkRow> EvidenceTransactionLinks
        => Set<EvidenceTransactionLinkRow>();

    internal DbSet<EvidenceCommandReceiptRow> EvidenceCommandReceipts
        => Set<EvidenceCommandReceiptRow>();

    internal DbSet<EvidenceObservationRow> EvidenceObservations
        => Set<EvidenceObservationRow>();

    internal DbSet<ReconciliationRunRow> ReconciliationRuns
        => Set<ReconciliationRunRow>();

    internal DbSet<ReconciliationItemRow> ReconciliationItems
        => Set<ReconciliationItemRow>();

    internal DbSet<ReconciliationItemResolutionRow> ReconciliationItemResolutions
        => Set<ReconciliationItemResolutionRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(SqliteConnectionPragmaInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CurrencyConfiguration());
        modelBuilder.ApplyConfiguration(new HouseholdConfiguration());
        modelBuilder.ApplyConfiguration(new HouseholdMemberConfiguration());
        modelBuilder.ApplyConfiguration(new InstitutionConfiguration());
        modelBuilder.ApplyConfiguration(new PortfolioConfiguration());
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new AssetConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CashFlowDetailConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionCostComponentConfiguration());
        modelBuilder.ApplyConfiguration(new AssetLotConfiguration());
        modelBuilder.ApplyConfiguration(new LotEntryAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new PhysicalGoldLotDetailConfiguration());
        modelBuilder.ApplyConfiguration(
            new PhysicalGoldLotAllocationDetailConfiguration());
        modelBuilder.ApplyConfiguration(
            new PhysicalGoldTradeDetailConfiguration());
        modelBuilder.ApplyConfiguration(new CommandReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenceRecordConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenceAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(
            new EvidenceTransactionLinkConfiguration());
        modelBuilder.ApplyConfiguration(
            new EvidenceCommandReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new EvidenceObservationConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationRunConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationItemConfiguration());
        modelBuilder.ApplyConfiguration(
            new ReconciliationItemResolutionConfiguration());
    }
}
