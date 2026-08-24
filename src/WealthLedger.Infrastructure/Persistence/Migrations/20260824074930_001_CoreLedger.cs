using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class _001_CoreLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Currency",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MinorUnitDigits = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currency", x => x.Code);
                    table.CheckConstraint("CK_Currency_Code", "length(\"Code\") = 3 AND \"Code\" GLOB '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("CK_Currency_MinorUnitDigits", "\"MinorUnitDigits\" BETWEEN 0 AND 8");
                });

            migrationBuilder.CreateTable(
                name: "Institution",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InstitutionTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institution", x => x.Id);
                    table.CheckConstraint("CK_Institution_IsActive", "\"IsActive\" IN (0, 1)");
                    table.CheckConstraint("CK_Institution_Type", "\"InstitutionTypeCode\" IN ('BANK', 'BROKER', 'ASSET_MANAGER', 'JEWELER', 'PENSION', 'OTHER')");
                });

            migrationBuilder.CreateTable(
                name: "Asset",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AssetTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseUnitCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    LotTrackingModeCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asset", x => x.Id);
                    table.CheckConstraint("CK_Asset_BaseUnit", "\"BaseUnitCode\" IN ('CURRENCY_UNIT', 'FUND_UNIT', 'SHARE', 'GROSS_GRAM', 'PIECE', 'PROPERTY', 'LAND_PARCEL', 'VEHICLE', 'OTHER')");
                    table.CheckConstraint("CK_Asset_IsActive", "\"IsActive\" IN (0, 1)");
                    table.CheckConstraint("CK_Asset_LotTrackingMode", "\"LotTrackingModeCode\" IN ('NONE', 'OPTIONAL', 'REQUIRED')");
                    table.CheckConstraint("CK_Asset_Type", "\"AssetTypeCode\" IN ('CASH', 'CURRENCY', 'FUND', 'EQUITY', 'PHYSICAL_GOLD', 'REAL_ESTATE', 'LAND', 'VEHICLE', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_Asset_Currency_BaseCurrencyCode",
                        column: x => x.BaseCurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Household",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Household", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Household_Currency_BaseCurrencyCode",
                        column: x => x.BaseCurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    InstitutionId = table.Column<string>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AccountTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    OpenedOn = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedOn = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.Id);
                    table.CheckConstraint("CK_Account_DateOrder", "\"OpenedOn\" IS NULL OR \"ClosedOn\" IS NULL OR \"OpenedOn\" <= \"ClosedOn\"");
                    table.CheckConstraint("CK_Account_IsActive", "\"IsActive\" IN (0, 1)");
                    table.CheckConstraint("CK_Account_Type", "\"AccountTypeCode\" IN ('CASH', 'INVESTMENT', 'PHYSICAL_VAULT', 'PENSION', 'PROPERTY_REGISTRY', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_Account_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Account_Institution_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdMember",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMember", x => x.Id);
                    table.CheckConstraint("CK_HouseholdMember_IsActive", "\"IsActive\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_HouseholdMember_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerTransaction",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OrderDate = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionDate = table.Column<string>(type: "TEXT", nullable: true),
                    SettlementDate = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReversalOfTransactionId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    PostedAtUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerTransaction", x => x.Id);
                    table.CheckConstraint("CK_LedgerTransaction_ExecutionSettlementDate", "\"ExecutionDate\" IS NULL OR \"SettlementDate\" IS NULL OR \"ExecutionDate\" <= \"SettlementDate\"");
                    table.CheckConstraint("CK_LedgerTransaction_Ordered", "\"StatusCode\" <> 'ORDERED' OR (\"TransactionTypeCode\" IN ('BUY', 'SELL') AND \"OrderDate\" IS NOT NULL)");
                    table.CheckConstraint("CK_LedgerTransaction_OrderExecutionDate", "\"OrderDate\" IS NULL OR \"ExecutionDate\" IS NULL OR \"OrderDate\" <= \"ExecutionDate\"");
                    table.CheckConstraint("CK_LedgerTransaction_PostedAt", "(\"StatusCode\" = 'POSTED' AND \"PostedAtUtc\" IS NOT NULL) OR (\"StatusCode\" <> 'POSTED' AND \"PostedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_LedgerTransaction_ReversalShape", "(\"TransactionTypeCode\" = 'REVERSAL' AND \"ReversalOfTransactionId\" IS NOT NULL) OR (\"TransactionTypeCode\" <> 'REVERSAL' AND \"ReversalOfTransactionId\" IS NULL)");
                    table.CheckConstraint("CK_LedgerTransaction_ReversalTarget", "\"ReversalOfTransactionId\" IS NULL OR \"ReversalOfTransactionId\" <> \"Id\"");
                    table.CheckConstraint("CK_LedgerTransaction_Status", "\"StatusCode\" IN ('DRAFT', 'ORDERED', 'POSTED', 'CANCELLED')");
                    table.CheckConstraint("CK_LedgerTransaction_Type", "\"TransactionTypeCode\" IN ('CONTRIBUTION', 'WITHDRAWAL', 'BUY', 'SELL', 'TRANSFER', 'DIVIDEND', 'INCOME', 'EXPENSE', 'FEE', 'TAX', 'CORPORATE_ACTION', 'OPENING_BALANCE', 'ADJUSTMENT', 'REVERSAL')");
                    table.ForeignKey(
                        name: "FK_LedgerTransaction_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerTransaction_LedgerTransaction_ReversalOfTransactionId",
                        column: x => x.ReversalOfTransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Portfolio",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolio", x => x.Id);
                    table.CheckConstraint("CK_Portfolio_ClosedAt", "(\"StatusCode\" = 'ACTIVE' AND \"ClosedAtUtc\" IS NULL) OR (\"StatusCode\" IN ('CLOSED', 'ARCHIVED') AND \"ClosedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_Portfolio_Status", "\"StatusCode\" IN ('ACTIVE', 'CLOSED', 'ARCHIVED')");
                    table.ForeignKey(
                        name: "FK_Portfolio_Household_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Household",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashFlowDetail",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    CashFlowCategoryCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    HouseholdMemberId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashFlowDetail", x => x.TransactionId);
                    table.CheckConstraint("CK_CashFlowDetail_Category", "\"CashFlowCategoryCode\" IN ('SALARY', 'BONUS', 'ACADEMIC_INCOME', 'GIFT', 'EXTERNAL_SALE', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_CashFlowDetail_HouseholdMember_HouseholdMemberId",
                        column: x => x.HouseholdMemberId,
                        principalTable: "HouseholdMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashFlowDetail_LedgerTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionCostComponent",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    CostTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TreatmentCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCostComponent", x => x.Id);
                    table.CheckConstraint("CK_TransactionCostComponent_Amount", "\"AmountMinor\" >= 0");
                    table.CheckConstraint("CK_TransactionCostComponent_Treatment", "\"TreatmentCode\" IN ('ADDITIONAL_CASH_OUTFLOW', 'WITHHELD_FROM_PROCEEDS', 'INCLUDED_IN_CONSIDERATION', 'INFORMATIONAL_ONLY')");
                    table.CheckConstraint("CK_TransactionCostComponent_Type", "\"CostTypeCode\" IN ('COMMISSION', 'WITHHOLDING_TAX', 'OTHER_TAX', 'MAKING_CHARGE', 'BROKERAGE', 'TITLE_DEED', 'EXPERTISE', 'NOTARY', 'INSURANCE', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_TransactionCostComponent_Currency_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionCostComponent_LedgerTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionEntry",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<string>(type: "TEXT", nullable: false),
                    EntrySequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PortfolioId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityDeltaE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    EntryRoleCode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    UnitPriceE8 = table.Column<long>(type: "INTEGER", nullable: true),
                    PriceCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionEntry", x => x.Id);
                    table.CheckConstraint("CK_TransactionEntry_Quantity", "\"QuantityDeltaE8\" <> 0");
                    table.CheckConstraint("CK_TransactionEntry_Role", "\"EntryRoleCode\" IN ('PRINCIPAL', 'CONSIDERATION', 'TRANSFER', 'INCOME', 'FEE', 'TAX', 'ADJUSTMENT')");
                    table.CheckConstraint("CK_TransactionEntry_Sequence", "\"EntrySequence\" >= 0");
                    table.CheckConstraint("CK_TransactionEntry_UnitPrice", "\"UnitPriceE8\" IS NULL OR \"UnitPriceE8\" >= 0");
                    table.CheckConstraint("CK_TransactionEntry_UnitPriceCurrency", "(\"UnitPriceE8\" IS NULL AND \"PriceCurrencyCode\" IS NULL) OR (\"UnitPriceE8\" IS NOT NULL AND \"PriceCurrencyCode\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TransactionEntry_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionEntry_Asset_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Asset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionEntry_Currency_PriceCurrencyCode",
                        column: x => x.PriceCurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionEntry_LedgerTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "LedgerTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionEntry_Portfolio_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetLot",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", nullable: false),
                    OpeningTransactionEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredOn = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalCostBasisMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    CostBasisCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    CostBasisStatusCode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetLot", x => x.Id);
                    table.CheckConstraint("CK_AssetLot_CostBasisAmount", "\"OriginalCostBasisMinor\" IS NULL OR \"OriginalCostBasisMinor\" >= 0");
                    table.CheckConstraint("CK_AssetLot_CostBasisShape", "(\"CostBasisStatusCode\" = 'KNOWN' AND \"OriginalCostBasisMinor\" IS NOT NULL AND \"CostBasisCurrencyCode\" IS NOT NULL) OR (\"CostBasisStatusCode\" IN ('UNKNOWN', 'NOT_APPLICABLE') AND \"OriginalCostBasisMinor\" IS NULL AND \"CostBasisCurrencyCode\" IS NULL)");
                    table.CheckConstraint("CK_AssetLot_CostBasisStatus", "\"CostBasisStatusCode\" IN ('KNOWN', 'UNKNOWN', 'NOT_APPLICABLE')");
                    table.ForeignKey(
                        name: "FK_AssetLot_Asset_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Asset",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetLot_Currency_CostBasisCurrencyCode",
                        column: x => x.CostBasisCurrencyCode,
                        principalTable: "Currency",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetLot_TransactionEntry_OpeningTransactionEntryId",
                        column: x => x.OpeningTransactionEntryId,
                        principalTable: "TransactionEntry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LotEntryAllocation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AssetLotId = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityDeltaE8 = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotEntryAllocation", x => x.Id);
                    table.CheckConstraint("CK_LotEntryAllocation_Quantity", "\"QuantityDeltaE8\" <> 0");
                    table.ForeignKey(
                        name: "FK_LotEntryAllocation_AssetLot_AssetLotId",
                        column: x => x.AssetLotId,
                        principalTable: "AssetLot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LotEntryAllocation_TransactionEntry_TransactionEntryId",
                        column: x => x.TransactionEntryId,
                        principalTable: "TransactionEntry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalGoldLotDetail",
                columns: table => new
                {
                    AssetLotId = table.Column<string>(type: "TEXT", nullable: false),
                    ActualFinenessPpm = table.Column<int>(type: "INTEGER", nullable: false),
                    PieceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Hallmark = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CertificateReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalGoldLotDetail", x => x.AssetLotId);
                    table.CheckConstraint("CK_PhysicalGoldLotDetail_Fineness", "\"ActualFinenessPpm\" > 0 AND \"ActualFinenessPpm\" <= 1000000");
                    table.CheckConstraint("CK_PhysicalGoldLotDetail_PieceCount", "\"PieceCount\" > 0");
                    table.ForeignKey(
                        name: "FK_PhysicalGoldLotDetail_AssetLot_AssetLotId",
                        column: x => x.AssetLotId,
                        principalTable: "AssetLot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_InstitutionId",
                table: "Account",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "UX_Account_Household_Code",
                table: "Account",
                columns: new[] { "HouseholdId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asset_BaseCurrencyCode",
                table: "Asset",
                column: "BaseCurrencyCode");

            migrationBuilder.CreateIndex(
                name: "UX_Asset_Code",
                table: "Asset",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetLot_Asset_Date",
                table: "AssetLot",
                columns: new[] { "AssetId", "AcquiredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetLot_CostBasisCurrencyCode",
                table: "AssetLot",
                column: "CostBasisCurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_AssetLot_OpeningTransactionEntryId",
                table: "AssetLot",
                column: "OpeningTransactionEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashFlowDetail_HouseholdMemberId",
                table: "CashFlowDetail",
                column: "HouseholdMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Household_BaseCurrencyCode",
                table: "Household",
                column: "BaseCurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMember_Household",
                table: "HouseholdMember",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "UX_Institution_Code",
                table: "Institution",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransaction_Household_Status_Date",
                table: "LedgerTransaction",
                columns: new[] { "HouseholdId", "StatusCode", "ExecutionDate" });

            migrationBuilder.CreateIndex(
                name: "UX_LedgerTransaction_Reversal",
                table: "LedgerTransaction",
                column: "ReversalOfTransactionId",
                unique: true,
                filter: "\"ReversalOfTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LotEntryAllocation_Entry",
                table: "LotEntryAllocation",
                column: "TransactionEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LotEntryAllocation_Lot",
                table: "LotEntryAllocation",
                column: "AssetLotId");

            migrationBuilder.CreateIndex(
                name: "UX_LotEntryAllocation_Lot_Entry",
                table: "LotEntryAllocation",
                columns: new[] { "AssetLotId", "TransactionEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Portfolio_Household_Code",
                table: "Portfolio",
                columns: new[] { "HouseholdId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCostComponent_CurrencyCode",
                table: "TransactionCostComponent",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCostComponent_Transaction",
                table: "TransactionCostComponent",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntry_Account_Asset",
                table: "TransactionEntry",
                columns: new[] { "AccountId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntry_AssetId",
                table: "TransactionEntry",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntry_Portfolio_Asset",
                table: "TransactionEntry",
                columns: new[] { "PortfolioId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntry_PriceCurrencyCode",
                table: "TransactionEntry",
                column: "PriceCurrencyCode");

            migrationBuilder.CreateIndex(
                name: "UX_TransactionEntry_Transaction_Sequence",
                table: "TransactionEntry",
                columns: new[] { "TransactionId", "EntrySequence" },
                unique: true);

            CreateCoreLedgerProtections(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropCoreLedgerProtections(migrationBuilder);

            migrationBuilder.DropTable(
                name: "CashFlowDetail");

            migrationBuilder.DropTable(
                name: "LotEntryAllocation");

            migrationBuilder.DropTable(
                name: "PhysicalGoldLotDetail");

            migrationBuilder.DropTable(
                name: "TransactionCostComponent");

            migrationBuilder.DropTable(
                name: "HouseholdMember");

            migrationBuilder.DropTable(
                name: "AssetLot");

            migrationBuilder.DropTable(
                name: "TransactionEntry");

            migrationBuilder.DropTable(
                name: "Account");

            migrationBuilder.DropTable(
                name: "Asset");

            migrationBuilder.DropTable(
                name: "LedgerTransaction");

            migrationBuilder.DropTable(
                name: "Portfolio");

            migrationBuilder.DropTable(
                name: "Institution");

            migrationBuilder.DropTable(
                name: "Household");

            migrationBuilder.DropTable(
                name: "Currency");
        }
    }
}
