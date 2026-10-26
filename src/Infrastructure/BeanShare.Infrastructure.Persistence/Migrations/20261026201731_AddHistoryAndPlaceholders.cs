using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryAndPlaceholders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlaceholder",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoUserId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MergeTransferAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludedFromLedgerCredit",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VoidedGrams",
                table: "Purchases",
                type: "numeric(10,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "HistoryImportId",
                table: "BillingPeriods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHistorical",
                table: "BillingPeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "HistoryImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalCupCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cups = table.Column<int>(type: "integer", nullable: false),
                    HistoryImportId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalCupCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalCupCounts_HistoryImports_HistoryImportId",
                        column: x => x.HistoryImportId,
                        principalTable: "HistoryImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "date", nullable: true),
                    PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Grams = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    HistoryImportId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalPurchases_HistoryImports_HistoryImportId",
                        column: x => x.HistoryImportId,
                        principalTable: "HistoryImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriods_HistoryImportId",
                table: "BillingPeriods",
                column: "HistoryImportId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalCupCounts_BillingPeriodId_UserId",
                table: "HistoricalCupCounts",
                columns: new[] { "BillingPeriodId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalCupCounts_HistoryImportId",
                table: "HistoricalCupCounts",
                column: "HistoryImportId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPurchases_BillingPeriodId",
                table: "HistoricalPurchases",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPurchases_HistoryImportId",
                table: "HistoricalPurchases",
                column: "HistoryImportId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryImports_SpaceId",
                table: "HistoryImports",
                column: "SpaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalCupCounts");

            migrationBuilder.DropTable(
                name: "HistoricalPurchases");

            migrationBuilder.DropTable(
                name: "HistoryImports");

            migrationBuilder.DropIndex(
                name: "IX_BillingPeriods_HistoryImportId",
                table: "BillingPeriods");

            migrationBuilder.DropColumn(
                name: "IsPlaceholder",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MergedIntoUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MergeTransferAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "ExcludedFromLedgerCredit",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "VoidedGrams",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "HistoryImportId",
                table: "BillingPeriods");

            migrationBuilder.DropColumn(
                name: "IsHistorical",
                table: "BillingPeriods");
        }
    }
}
