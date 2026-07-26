using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseBarcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Purchases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_CoffeeStockId_Barcode",
                table: "Purchases",
                columns: new[] { "CoffeeStockId", "Barcode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchases_CoffeeStockId_Barcode",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Purchases");
        }
    }
}
