using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <inheritdoc />
    public partial class KitchenUsabilityImprovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MealId",
                table: "MealPlanItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KitchenTeamId",
                table: "KitchenSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "Ingredients",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BreakfastServe",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "07:00");

            migrationBuilder.AddColumn<string>(
                name: "BreakfastStart",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "05:00");

            migrationBuilder.AddColumn<string>(
                name: "DinnerServe",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "18:00");

            migrationBuilder.AddColumn<string>(
                name: "DinnerStart",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "15:00");

            migrationBuilder.AddColumn<string>(
                name: "LunchServe",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "13:00");

            migrationBuilder.AddColumn<string>(
                name: "LunchStart",
                table: "BoardingSettings",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "10:00");

            migrationBuilder.CreateTable(
                name: "KitchenTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTeams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Meals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MealType = table.Column<int>(type: "int", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DocumentContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CapturedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTeamMembers",
                columns: table => new
                {
                    KitchenTeamId = table.Column<int>(type: "int", nullable: false),
                    KitchenStaffMemberId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTeamMembers", x => new { x.KitchenTeamId, x.KitchenStaffMemberId });
                    table.ForeignKey(
                        name: "FK_KitchenTeamMembers_KitchenStaffMembers_KitchenStaffMemberId",
                        column: x => x.KitchenStaffMemberId,
                        principalTable: "KitchenStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KitchenTeamMembers_KitchenTeams_KitchenTeamId",
                        column: x => x.KitchenTeamId,
                        principalTable: "KitchenTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MealId = table.Column<int>(type: "int", nullable: false),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    QuantityPerServing = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealIngredients_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealIngredients_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockReceiptId = table.Column<int>(type: "int", nullable: false),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReceiptLines_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReceiptLines_StockReceipts_StockReceiptId",
                        column: x => x.StockReceiptId,
                        principalTable: "StockReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealPlanItems_MealId",
                table: "MealPlanItems",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenSchedules_KitchenTeamId",
                table: "KitchenSchedules",
                column: "KitchenTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTeamMembers_KitchenStaffMemberId",
                table: "KitchenTeamMembers",
                column: "KitchenStaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_IngredientId",
                table: "MealIngredients",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_MealId",
                table: "MealIngredients",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_Name",
                table: "Meals",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceiptLines_IngredientId",
                table: "StockReceiptLines",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceiptLines_StockReceiptId",
                table: "StockReceiptLines",
                column: "StockReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_ReceiptDate",
                table: "StockReceipts",
                column: "ReceiptDate");

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenSchedules_KitchenTeams_KitchenTeamId",
                table: "KitchenSchedules",
                column: "KitchenTeamId",
                principalTable: "KitchenTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MealPlanItems_Meals_MealId",
                table: "MealPlanItems",
                column: "MealId",
                principalTable: "Meals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KitchenSchedules_KitchenTeams_KitchenTeamId",
                table: "KitchenSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_MealPlanItems_Meals_MealId",
                table: "MealPlanItems");

            migrationBuilder.DropTable(
                name: "KitchenTeamMembers");

            migrationBuilder.DropTable(
                name: "MealIngredients");

            migrationBuilder.DropTable(
                name: "StockReceiptLines");

            migrationBuilder.DropTable(
                name: "KitchenTeams");

            migrationBuilder.DropTable(
                name: "Meals");

            migrationBuilder.DropTable(
                name: "StockReceipts");

            migrationBuilder.DropIndex(
                name: "IX_MealPlanItems_MealId",
                table: "MealPlanItems");

            migrationBuilder.DropIndex(
                name: "IX_KitchenSchedules_KitchenTeamId",
                table: "KitchenSchedules");

            migrationBuilder.DropColumn(
                name: "MealId",
                table: "MealPlanItems");

            migrationBuilder.DropColumn(
                name: "KitchenTeamId",
                table: "KitchenSchedules");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "BreakfastServe",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "BreakfastStart",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "DinnerServe",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "DinnerStart",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "LunchServe",
                table: "BoardingSettings");

            migrationBuilder.DropColumn(
                name: "LunchStart",
                table: "BoardingSettings");
        }
    }
}
