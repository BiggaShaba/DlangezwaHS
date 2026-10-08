using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <inheritdoc />
    public partial class KitchenUsagePrepTimeFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IngredientUsageRecordId",
                table: "StockUsageLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrepMinutesPerServing",
                table: "Meals",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNotes",
                table: "MealQualityAlerts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUserId",
                table: "MealQualityAlerts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Portion",
                table: "MealFeedbacks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PortionsPlanned",
                table: "KitchenSchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StaffRecommended",
                table: "KitchenSchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IngredientUsageRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MealPlanItemId = table.Column<int>(type: "int", nullable: false),
                    ServingsPrepared = table.Column<int>(type: "int", nullable: false),
                    LeftoverServings = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientUsageRecords_MealPlanItems_MealPlanItemId",
                        column: x => x.MealPlanItemId,
                        principalTable: "MealPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockUsageLogs_IngredientUsageRecordId",
                table: "StockUsageLogs",
                column: "IngredientUsageRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageRecords_MealPlanItemId",
                table: "IngredientUsageRecords",
                column: "MealPlanItemId");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientUsageRecords_RecordedAt",
                table: "IngredientUsageRecords",
                column: "RecordedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_StockUsageLogs_IngredientUsageRecords_IngredientUsageRecordId",
                table: "StockUsageLogs",
                column: "IngredientUsageRecordId",
                principalTable: "IngredientUsageRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockUsageLogs_IngredientUsageRecords_IngredientUsageRecordId",
                table: "StockUsageLogs");

            migrationBuilder.DropTable(
                name: "IngredientUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_StockUsageLogs_IngredientUsageRecordId",
                table: "StockUsageLogs");

            migrationBuilder.DropColumn(
                name: "IngredientUsageRecordId",
                table: "StockUsageLogs");

            migrationBuilder.DropColumn(
                name: "PrepMinutesPerServing",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "ResolutionNotes",
                table: "MealQualityAlerts");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "MealQualityAlerts");

            migrationBuilder.DropColumn(
                name: "Portion",
                table: "MealFeedbacks");

            migrationBuilder.DropColumn(
                name: "PortionsPlanned",
                table: "KitchenSchedules");

            migrationBuilder.DropColumn(
                name: "StaffRecommended",
                table: "KitchenSchedules");
        }
    }
}
