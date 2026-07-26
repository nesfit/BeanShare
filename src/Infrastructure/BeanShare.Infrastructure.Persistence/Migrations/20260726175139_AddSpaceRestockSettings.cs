using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpaceRestockSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RestockLeadTimeDays",
                table: "Spaces",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<decimal>(
                name: "RestockMinOrderValue",
                table: "Spaces",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 2000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestockLeadTimeDays",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "RestockMinOrderValue",
                table: "Spaces");
        }
    }
}
