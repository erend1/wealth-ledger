using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _006_OpeningBalanceCutoverGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ValidateExistingOpeningBalances(migrationBuilder);
        AddOpeningBalanceScopeIndex(migrationBuilder);
        AddOpeningBalancePostingTrigger(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropOpeningBalancePostingTrigger(migrationBuilder);
        DropOpeningBalanceScopeIndex(migrationBuilder);
    }
}
