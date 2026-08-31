using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _003_ReversalDependencySemantics: Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ReplacePostingTriggerWithNeutralizedDependencySemantics(
            migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RestoreLegacyPostingTrigger(
            migrationBuilder);
    }
}