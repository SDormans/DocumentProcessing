using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueReviewPerDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentReviews_DocumentId",
                table: "DocumentReviews");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "DocumentReviews",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // Earlier versions could insert several reviews for one document; keep the oldest so the unique index can be created.
            migrationBuilder.Sql("""
                DELETE FROM "DocumentReviews" r
                USING "DocumentReviews" keep
                WHERE r."DocumentId" = keep."DocumentId"
                  AND (keep."CreatedAtUtc", keep."Id") < (r."CreatedAtUtc", r."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviews_DocumentId",
                table: "DocumentReviews",
                column: "DocumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentReviews_DocumentId",
                table: "DocumentReviews");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "DocumentReviews");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviews_DocumentId",
                table: "DocumentReviews",
                column: "DocumentId");
        }
    }
}
