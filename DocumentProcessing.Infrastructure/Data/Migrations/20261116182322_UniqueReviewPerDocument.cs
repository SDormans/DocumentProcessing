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

            // Refuse to guess which of several reviews per document is authoritative; deleting one would also cascade its audit trail.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "DocumentReviews" GROUP BY "DocumentId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'DocumentReviews contains several reviews for the same document. Resolve them manually (SELECT "DocumentId" FROM "DocumentReviews" GROUP BY "DocumentId" HAVING COUNT(*) > 1) before applying UniqueReviewPerDocument.';
                    END IF;
                END $$;
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
