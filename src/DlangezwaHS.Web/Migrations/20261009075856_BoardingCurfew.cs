using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <inheritdoc />
    public partial class BoardingCurfew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurfewEnd",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "06:00");

            migrationBuilder.AddColumn<string>(
                name: "CurfewStart",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "19:00");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurfewEnd",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "CurfewStart",
                table: "BoardingSettings");
        }
    }
}
