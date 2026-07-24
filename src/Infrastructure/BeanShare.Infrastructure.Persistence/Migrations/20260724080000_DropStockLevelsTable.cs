using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Follow-up to <c>AddLotGrainConsumptionCostAndQrBag</c>: now that stock levels are a derived
    /// projection (computed from Purchases/lots), the legacy <c>StockLevels</c> table is orphaned —
    /// no code reads or writes it. This migration drops it.
    ///
    /// WARNING: effectively one-way. The old merged per-product totals (TotalPurchasedGrams /
    /// TotalConsumedGrams) are NOT reconstructable from the per-bag data, so <see cref="Down"/> only
    /// recreates an EMPTY table with the original shape. Take a database backup before applying this
    /// in production. Kept as a separate migration so per-bag tracking can be validated live first.
    /// </summary>
    /// <inheritdoc />
    public partial class DropStockLevelsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockLevels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort rollback: recreates the table structure only. Historical rows cannot be
            // restored from per-bag data — restore from a backup if you need the old contents.
            migrationBuilder.CreateTable(
                name: "StockLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductBrand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductType = table.Column<string>(type: "text", nullable: false),
                    TotalPurchasedGrams = table.Column<decimal>(type: "numeric(10,1)", nullable: false),
                    TotalConsumedGrams = table.Column<decimal>(type: "numeric(10,1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CoffeeStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsCurrentlyUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLevels_CoffeeStocks_CoffeeStockId",
                        column: x => x.CoffeeStockId,
                        principalTable: "CoffeeStocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockLevels_CoffeeStockId",
                table: "StockLevels",
                column: "CoffeeStockId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLevels_UpdatedAt",
                table: "StockLevels",
                column: "UpdatedAt");
        }
    }
}
