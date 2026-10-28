using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _010_ReconciliationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenceObservation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceId = table.Column<string>(type: "TEXT", nullable: false),
                    PortfolioId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedQuantityE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    ToleranceE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedPieceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    RecordedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceObservation", x => x.Id);
                    table.CheckConstraint("CK_EvidenceObservation_PieceCount", "\"ObservedPieceCount\" IS NULL OR \"ObservedPieceCount\" BETWEEN 0 AND 2147483647");
                    table.CheckConstraint("CK_EvidenceObservation_Tolerance", "\"ToleranceE8\" >= 0");
                    table.ForeignKey(
                        name: "FK_EvidenceObservation_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceObservation_Asset_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Asset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceObservation_EvidenceRecord_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceObservation_Portfolio_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRun",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceId = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceEffectiveDate = table.Column<string>(type: "TEXT", nullable: false),
                    RunAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    MethodCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MethodVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRun", x => x.Id);
                    table.CheckConstraint("CK_ReconciliationRun_IdempotencyKey_Length", "length(\"IdempotencyKey\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_ReconciliationRun_ItemCount", "\"ItemCount\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ReconciliationRun_Method", "\"MethodCode\" IN ('EXACT_SCOPE_QUANTITY_AS_OF')");
                    table.CheckConstraint("CK_ReconciliationRun_MethodVersion", "\"MethodVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_ReconciliationRun_EvidenceRecord_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationRun_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationItem",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservationId = table.Column<string>(type: "TEXT", nullable: false),
                    PortfolioId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedQuantityE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    LedgerQuantityE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    DeltaE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    ToleranceE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedPieceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LedgerPieceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    PieceDelta = table.Column<int>(type: "INTEGER", nullable: true),
                    ComparisonStatusCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationItem", x => x.Id);
                    table.CheckConstraint("CK_ReconciliationItem_ComparisonStatus", "\"ComparisonStatusCode\" IN ('MATCHED', 'OUT_OF_TOLERANCE') AND (\"ComparisonStatusCode\" = 'MATCHED') = (\"DeltaE8\" BETWEEN -\"ToleranceE8\" AND \"ToleranceE8\" AND coalesce(\"PieceDelta\", 0) = 0)");
                    table.CheckConstraint("CK_ReconciliationItem_Delta", "\"DeltaE8\" = \"ObservedQuantityE8\" - \"LedgerQuantityE8\"");
                    table.CheckConstraint("CK_ReconciliationItem_Pieces", "(\"ObservedPieceCount\" IS NULL AND \"LedgerPieceCount\" IS NULL AND \"PieceDelta\" IS NULL) OR (\"ObservedPieceCount\" IS NOT NULL AND \"LedgerPieceCount\" IS NOT NULL AND \"PieceDelta\" IS NOT NULL AND \"ObservedPieceCount\" BETWEEN 0 AND 2147483647 AND \"PieceDelta\" = \"ObservedPieceCount\" - \"LedgerPieceCount\")");
                    table.CheckConstraint("CK_ReconciliationItem_Sequence", "\"Sequence\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ReconciliationItem_Tolerance", "\"ToleranceE8\" >= 0");
                    table.ForeignKey(
                        name: "FK_ReconciliationItem_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationItem_Asset_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Asset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationItem_EvidenceObservation_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "EvidenceObservation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationItem_Portfolio_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationItem_ReconciliationRun_RunId",
                        column: x => x.RunId,
                        principalTable: "ReconciliationRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationItemResolution",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    ResolutionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CorrectiveTransactionId = table.Column<string>(type: "TEXT", nullable: true),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ResolvedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationItemResolution", x => x.ItemId);
                    table.CheckConstraint("CK_ReconciliationItemResolution_Shape", "(\"ResolutionCode\" = 'CORRECTED_BY_TRANSACTION' AND \"CorrectiveTransactionId\" IS NOT NULL AND \"Explanation\" IS NULL) OR (\"ResolutionCode\" = 'DOCUMENTED_EXCEPTION' AND \"CorrectiveTransactionId\" IS NULL AND \"Explanation\" IS NOT NULL AND length(\"Explanation\") BETWEEN 1 AND 2000)");
                    table.ForeignKey(
                        name: "FK_ReconciliationItemResolution_LedgerTransaction_CorrectiveTransactionId",
                        column: x => x.CorrectiveTransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationItemResolution_ReconciliationItem_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ReconciliationItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceObservation_Account",
                table: "EvidenceObservation",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceObservation_Asset",
                table: "EvidenceObservation",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceObservation_Portfolio",
                table: "EvidenceObservation",
                column: "PortfolioId");

            migrationBuilder.CreateIndex(
                name: "UX_EvidenceObservation_Evidence_Scope",
                table: "EvidenceObservation",
                columns: new[] { "EvidenceId", "PortfolioId", "AccountId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationItem_Account",
                table: "ReconciliationItem",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationItem_Asset",
                table: "ReconciliationItem",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationItem_Observation",
                table: "ReconciliationItem",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationItem_Portfolio",
                table: "ReconciliationItem",
                column: "PortfolioId");

            migrationBuilder.CreateIndex(
                name: "UX_ReconciliationItem_Run_Observation",
                table: "ReconciliationItem",
                columns: new[] { "RunId", "ObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ReconciliationItem_Run_Sequence",
                table: "ReconciliationItem",
                columns: new[] { "RunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationItemResolution_CorrectiveTransaction",
                table: "ReconciliationItemResolution",
                column: "CorrectiveTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRun_Evidence_RunAt_Id",
                table: "ReconciliationRun",
                columns: new[] { "EvidenceId", "RunAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRun_Household_RunAt_Id",
                table: "ReconciliationRun",
                columns: new[] { "HouseholdId", "RunAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "UX_ReconciliationRun_Household_IdempotencyKey",
                table: "ReconciliationRun",
                columns: new[] { "HouseholdId", "IdempotencyKey" },
                unique: true);

            AddReconciliationGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropReconciliationGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "ReconciliationItemResolution");

            migrationBuilder.DropTable(
                name: "ReconciliationItem");

            migrationBuilder.DropTable(
                name: "EvidenceObservation");

            migrationBuilder.DropTable(
                name: "ReconciliationRun");
        }
    }
}
