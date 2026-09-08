using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations;

public partial class _005_WorkspaceIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        CreateWorkspaceIdentity(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropWorkspaceIdentity(migrationBuilder);
    }
}
