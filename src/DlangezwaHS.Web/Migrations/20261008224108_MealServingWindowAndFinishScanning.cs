using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <inheritdoc />
    public partial class MealServingWindowAndFinishScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScanningFinishedAt",
                table: "MealPlanItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServingMinutes",
                table: "BoardingSettings",
                type: "int",
                nullable: false,
                defaultValue: 90);

            // Meals already closed with the old "Close Collection" button have usage records:
            // mark them finished so they don't reopen for scanning
            migrationBuilder.Sql(@"
                UPDATE i SET ScanningFinishedAt = r.RecordedAt
                FROM MealPlanItems i
                JOIN (SELECT MealPlanItemId, MAX(RecordedAt) AS RecordedAt FROM IngredientUsageRecords GROUP BY MealPlanItemId) r
                    ON r.MealPlanItemId = i.Id
                WHERE i.ScanningFinishedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScanningFinishedAt",
                table: "MealPlanItems");

            migrationBuilder.DropColumn(
                name: "ServingMinutes",
                table: "BoardingSettings");
        }
    }
}
