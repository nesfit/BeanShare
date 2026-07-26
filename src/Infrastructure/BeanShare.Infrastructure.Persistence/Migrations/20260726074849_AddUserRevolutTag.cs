using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanShare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRevolutTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevolutTag",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevolutTag",
                table: "Users");
        }
    }
}
