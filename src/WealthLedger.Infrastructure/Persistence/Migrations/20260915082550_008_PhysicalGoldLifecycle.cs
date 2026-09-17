using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _008_PhysicalGoldLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ValidateExistingPhysicalGoldHistory(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "PhysicalGoldLotAllocationDetail",
                columns: table => new
                {
                    LotEntryAllocationId = table.Column<string>(type: "TEXT", nullable: false),
                    PieceDelta = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalGoldLotAllocationDetail", x => x.LotEntryAllocationId);
                    table.CheckConstraint("CK_PhysicalGoldLotAllocationDetail_PieceDelta", "\"PieceDelta\" <> 0 AND \"PieceDelta\" BETWEEN -2147483647 AND 2147483647");
                    table.ForeignKey(
                        name: "FK_PhysicalGoldLotAllocationDetail_LotEntryAllocation_LotEntryAllocationId",
                        column: x => x.LotEntryAllocationId,
                        principalTable: "LotEntryAllocation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalGoldTradeDetail",
                columns: table => new
                {
                    LedgerTransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    CounterpartyInstitutionId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalGoldTradeDetail", x => x.LedgerTransactionId);
                    table.ForeignKey(
                        name: "FK_PhysicalGoldTradeDetail_Institution_CounterpartyInstitutionId",
                        column: x => x.CounterpartyInstitutionId,
                        principalTable: "Institution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalGoldTradeDetail_LedgerTransaction_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalGoldTradeDetail_CounterpartyInstitutionId",
                table: "PhysicalGoldTradeDetail",
                column: "CounterpartyInstitutionId");

            BackfillPhysicalGoldPieceMovement(migrationBuilder);
            AddPhysicalGoldLifecycleGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropPhysicalGoldLifecycleGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "PhysicalGoldLotAllocationDetail");

            migrationBuilder.DropTable(
                name: "PhysicalGoldTradeDetail");
        }
    }
}
