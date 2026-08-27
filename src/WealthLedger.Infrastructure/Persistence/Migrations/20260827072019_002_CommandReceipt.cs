using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _002_CommandReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandReceipt",
                columns: table => new
                {
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    FingerprintAlgorithmCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FingerprintVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    FingerprintValue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ResultTransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    ResultAssetLotId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandReceipt", x => new { x.HouseholdId, x.OperationCode, x.IdempotencyKey });
                    table.CheckConstraint("CK_CommandReceipt_FingerprintAlgorithm_Length", "length(\"FingerprintAlgorithmCode\") BETWEEN 1 AND 32");
                    table.CheckConstraint("CK_CommandReceipt_FingerprintValue_Length", "length(\"FingerprintValue\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_CommandReceipt_FingerprintVersion", "\"FingerprintVersion\" >= 1");
                    table.CheckConstraint("CK_CommandReceipt_IdempotencyKey_Length", "length(\"IdempotencyKey\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_CommandReceipt_OperationCode_Length", "length(\"OperationCode\") BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "FK_CommandReceipt_AssetLot_ResultAssetLotId",
                        column: x => x.ResultAssetLotId,
                        principalTable: "AssetLot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommandReceipt_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommandReceipt_LedgerTransaction_ResultTransactionId",
                        column: x => x.ResultTransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandReceipt_ResultAssetLot",
                table: "CommandReceipt",
                column: "ResultAssetLotId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandReceipt_ResultTransaction",
                table: "CommandReceipt",
                column: "ResultTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandReceipt");
        }
    }
}
