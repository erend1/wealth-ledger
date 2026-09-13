using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the M008 fund-trade posting guards.
    /// </summary>
    /// <remarks>
    /// Additive only. No historical migration is edited, no M003 or M007
    /// guard is weakened, and no realized-cost, remaining-quantity,
    /// current-position or valuation table is introduced.
    ///
    /// Down removes only the objects this migration created, restoring the
    /// exact behaviour of migration 006.
    /// </remarks>
    public partial class _007_FundTradeLifecycleGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddFundTradeGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropFundTradeGuards(migrationBuilder);
        }
    }
}
