using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _004_LedgerNavigationQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransaction_Household_Status_Posted_Id",
                table: "LedgerTransaction",
                columns: new[] { "HouseholdId", "StatusCode", "PostedAtUtc", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerTransaction_Household_Status_Posted_Id",
                table: "LedgerTransaction");
        }
    }
}
