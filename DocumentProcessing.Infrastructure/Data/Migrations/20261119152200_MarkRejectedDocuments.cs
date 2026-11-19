using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarkRejectedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rejected documents used to be stored as Failed (5); move them to Rejected (6).
            migrationBuilder.Sql("""
                UPDATE "Documents" AS d SET "Status" = 6
                WHERE d."Status" = 5
                  AND EXISTS (SELECT 1 FROM "DocumentReviews" AS r WHERE r."DocumentId" = d."Id" AND r."Status" = 3);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "Documents" SET "Status" = 5 WHERE "Status" = 6;""");
        }
    }
}
