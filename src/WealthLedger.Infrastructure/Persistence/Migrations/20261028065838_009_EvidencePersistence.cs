using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _009_EvidencePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenceRecord",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKindCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EffectiveDate = table.Column<string>(type: "TEXT", nullable: false),
                    InstitutionId = table.Column<string>(type: "TEXT", nullable: true),
                    AccountId = table.Column<string>(type: "TEXT", nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SupersedesEvidenceId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceRecord", x => x.Id);
                    table.CheckConstraint("CK_EvidenceRecord_Label_Length", "length(\"Label\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_EvidenceRecord_Note_Length", "\"Note\" IS NULL OR length(\"Note\") BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_EvidenceRecord_Reference_Length", "\"Reference\" IS NULL OR length(\"Reference\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_EvidenceRecord_SourceKind", "\"SourceKindCode\" IN ('STATEMENT', 'RECEIPT', 'PHYSICAL_COUNT', 'CONTRACT_OR_CERTIFICATE', 'OTHER')");
                    table.CheckConstraint("CK_EvidenceRecord_SupersedesOther", "\"SupersedesEvidenceId\" IS NULL OR \"SupersedesEvidenceId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_EvidenceRecord_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceRecord_EvidenceRecord_SupersedesEvidenceId",
                        column: x => x.SupersedesEvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceRecord_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceRecord_Institution_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceAttachment",
                columns: table => new
                {
                    EvidenceId = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MediaTypeCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceAttachment", x => x.EvidenceId);
                    table.CheckConstraint("CK_EvidenceAttachment_Content", "typeof(\"Content\") = 'blob' AND \"ByteLength\" BETWEEN 1 AND 10485760 AND \"ByteLength\" = length(\"Content\")");
                    table.CheckConstraint("CK_EvidenceAttachment_FileName_Length", "length(\"FileName\") BETWEEN 1 AND 255");
                    table.CheckConstraint("CK_EvidenceAttachment_MediaType", "\"MediaTypeCode\" IN ('PDF', 'PNG', 'JPEG', 'PLAIN_TEXT', 'CSV')");
                    table.CheckConstraint("CK_EvidenceAttachment_Sha256", "length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_EvidenceAttachment_EvidenceRecord_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceCommandReceipt",
                columns: table => new
                {
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    OperationCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    FingerprintAlgorithmCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FingerprintVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    FingerprintValue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ResultEvidenceId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceCommandReceipt", x => new { x.HouseholdId, x.OperationCode, x.IdempotencyKey });
                    table.CheckConstraint("CK_EvidenceCommandReceipt_FingerprintAlgorithm_Length", "length(\"FingerprintAlgorithmCode\") BETWEEN 1 AND 32");
                    table.CheckConstraint("CK_EvidenceCommandReceipt_FingerprintValue_Length", "length(\"FingerprintValue\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_EvidenceCommandReceipt_FingerprintVersion", "\"FingerprintVersion\" >= 1");
                    table.CheckConstraint("CK_EvidenceCommandReceipt_IdempotencyKey_Length", "length(\"IdempotencyKey\") BETWEEN 1 AND 256");
                    table.CheckConstraint("CK_EvidenceCommandReceipt_OperationCode_Length", "length(\"OperationCode\") BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "FK_EvidenceCommandReceipt_EvidenceRecord_ResultEvidenceId",
                        column: x => x.ResultEvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceCommandReceipt_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceTransactionLink",
                columns: table => new
                {
                    EvidenceId = table.Column<string>(type: "TEXT", nullable: false),
                    LedgerTransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceTransactionLink", x => new { x.EvidenceId, x.LedgerTransactionId });
                    table.ForeignKey(
                        name: "FK_EvidenceTransactionLink_EvidenceRecord_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "EvidenceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceTransactionLink_LedgerTransaction_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceCommandReceipt_ResultEvidence",
                table: "EvidenceCommandReceipt",
                column: "ResultEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRecord_Account",
                table: "EvidenceRecord",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRecord_Household_Effective_Created_Id",
                table: "EvidenceRecord",
                columns: new[] { "HouseholdId", "EffectiveDate", "CreatedAtUtc", "Id" },
                descending: new[] { false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceRecord_Institution",
                table: "EvidenceRecord",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "UX_EvidenceRecord_Supersedes",
                table: "EvidenceRecord",
                column: "SupersedesEvidenceId",
                unique: true,
                filter: "\"SupersedesEvidenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceTransactionLink_Transaction",
                table: "EvidenceTransactionLink",
                column: "LedgerTransactionId");

            AddEvidenceGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropEvidenceGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "EvidenceAttachment");

            migrationBuilder.DropTable(
                name: "EvidenceCommandReceipt");

            migrationBuilder.DropTable(
                name: "EvidenceTransactionLink");

            migrationBuilder.DropTable(
                name: "EvidenceRecord");
        }
    }
}
