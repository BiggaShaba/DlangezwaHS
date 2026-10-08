using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DlangezwaHS.Web.Migrations
{
    /// <inheritdoc />
    public partial class DietaryProfilesNoReviewWithDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DietaryProfileDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DietaryProfileId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietaryProfileDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DietaryProfileDocuments_DietaryProfiles_DietaryProfileId",
                        column: x => x.DietaryProfileId,
                        principalTable: "DietaryProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // No more review step: any profile still waiting (Pending / PendingParent) becomes active
            // if it is the learner's newest; older waiting or active ones are replaced (Superseded).
            migrationBuilder.Sql(@"
;WITH ranked AS (
    SELECT Status, ROW_NUMBER() OVER (PARTITION BY LearnerId ORDER BY SubmittedAt DESC, Id DESC) AS rn
    FROM DietaryProfiles
    WHERE Status IN (0, 1, 4)
)
UPDATE ranked SET Status = CASE WHEN rn = 1 THEN 1 ELSE 3 END;");

            migrationBuilder.CreateIndex(
                name: "IX_DietaryProfileDocuments_DietaryProfileId",
                table: "DietaryProfileDocuments",
                column: "DietaryProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DietaryProfileDocuments");
        }
    }
}
