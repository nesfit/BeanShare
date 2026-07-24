using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotGrainConsumptionCostAndQrBag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the legacy "StockLevels" table is intentionally NOT dropped here. Stock levels are
            // now a derived projection (computed from Purchases), so the table is orphaned — but it is
            // left in place so this migration stays reversible and the per-bag back-fill can be validated
            // live first. A separate follow-up migration drops it once we're confident (see plan Phase 5).

            migrationBuilder.AddColumn<decimal>(
                name: "ConsumedGrams",
                table: "Purchases",
                type: "numeric(10,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepletedAt",
                table: "Purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Purchases",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "Purchases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Purchases",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sealed");

            migrationBuilder.AddColumn<decimal>(
                name: "CostAmount",
                table: "Consumptions",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCurrency",
                table: "Consumptions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockLotId",
                table: "Consumptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostPerGram",
                table: "Consumptions",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockLotId",
                table: "ActiveQrCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_CoffeeStockId_PurchasedAt",
                table: "Purchases",
                columns: new[] { "CoffeeStockId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_StockLotId",
                table: "Consumptions",
                column: "StockLotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchases_CoffeeStockId_PurchasedAt",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Consumptions_StockLotId",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "ConsumedGrams",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "DepletedAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CostAmount",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "CostCurrency",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "StockLotId",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "UnitCostPerGram",
                table: "Consumptions");

            migrationBuilder.DropColumn(
                name: "StockLotId",
                table: "ActiveQrCodes");

            // StockLevels is intentionally left untouched by Up (see note there), so Down has nothing
            // to recreate.
        }
    }
}
