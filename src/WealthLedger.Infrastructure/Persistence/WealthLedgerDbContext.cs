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
    }
}
