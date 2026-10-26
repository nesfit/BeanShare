using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LedgerStartDate",
                table: "Spaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LedgerVersion",
                table: "Spaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TreasurerUserId",
                table: "Spaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Model",
                table: "Settlements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "Settlements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededBySettlementId",
                table: "Settlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TreasurerUserId",
                table: "Settlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Settlements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentsAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CarriedFromSettlementId",
                table: "SettlementLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarriedInAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CarriedOutAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CarriedToSettlementId",
                table: "SettlementLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LegacyAmountPaid",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Resolution",
                table: "SettlementLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundingAmount",
                table: "SettlementLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Purchases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Consumptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "BillingPeriods",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BalanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IncludedInSettlementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceEntries", x => x.Id);
                    table.CheckConstraint("CK_BalanceEntries_Amount_NonZero", "\"Amount\" <> 0");
                    table.CheckConstraint("CK_BalanceEntries_Parties_Differ", "\"UserId\" <> \"CounterpartyUserId\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_SpaceId_Sequence",
                table: "Settlements",
                columns: new[] { "SpaceId", "Sequence" },
                unique: true,
                filter: "\"Sequence\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_CarriedFromSettlementId_UserId",
                table: "SettlementLines",
                columns: new[] { "CarriedFromSettlementId", "UserId" },
                unique: true,
                filter: "\"CarriedFromSettlementId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BalanceEntries_SpaceId_CounterpartyUserId",
                table: "BalanceEntries",
                columns: new[] { "SpaceId", "CounterpartyUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceEntries_SpaceId_IncludedInSettlementId",
                table: "BalanceEntries",
                columns: new[] { "SpaceId", "IncludedInSettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceEntries_SpaceId_UserId",
                table: "BalanceEntries",
                columns: new[] { "SpaceId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceEntries");

            migrationBuilder.DropIndex(
                name: "IX_Settlements_SpaceId_Sequence",
                table: "Settlements");

            migrationBuilder.DropIndex(
                name: "IX_SettlementLines_CarriedFromSettlementId_UserId",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "LedgerStartDate",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "LedgerVersion",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "TreasurerUserId",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "SupersededBySettlementId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "TreasurerUserId",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Settlements");

            migrationBuilder.DropColumn(
                name: "AdjustmentsAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "CarriedFromSettlementId",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "CarriedInAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "CarriedOutAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "CarriedToSettlementId",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "CreditAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "LegacyAmountPaid",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "RoundingAmount",
                table: "SettlementLines");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "BillingPeriods");
        }
    }
}
