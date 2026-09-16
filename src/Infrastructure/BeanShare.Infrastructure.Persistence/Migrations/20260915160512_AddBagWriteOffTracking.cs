using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBagWriteOffTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WrittenOffGrams",
                table: "Purchases",
                type: "numeric(10,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsWriteOff",
                table: "Consumptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: phantom-fill cups written so far are recognisable only by the default reason
            // MarkBagEmpty stamps into PresetName (no preset behind them). Flag them, and give each
            // still-empty bag the total it wrote off, so a recent emptying is undoable after upgrade.
            migrationBuilder.Sql("""
                UPDATE "Consumptions"
                SET "IsWriteOff" = TRUE
                WHERE "PresetId" IS NULL
                  AND "StockLotId" IS NOT NULL
                  AND "PresetName" = 'Bag emptied';

                UPDATE "Purchases" p
                SET "WrittenOffGrams" = w.grams
                FROM (
                    SELECT "StockLotId", SUM("QuantityGrams") AS grams
                    FROM "Consumptions"
                    WHERE "IsWriteOff"
                    GROUP BY "StockLotId"
                ) w
                WHERE w."StockLotId" = p."Id"
                  AND p."Status" = 'Empty';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WrittenOffGrams",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "IsWriteOff",
                table: "Consumptions");
        }
    }
}
