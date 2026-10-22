using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncAppDbContextModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTextExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionVersion = table.Column<int>(type: "integer", nullable: false),
                    ExtractionMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FullText = table.Column<string>(type: "text", nullable: false),
                    TotalCharacterCount = table.Column<int>(type: "integer", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    HasAnyText = table.Column<bool>(type: "boolean", nullable: false),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false),
                    ExtractedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QualityJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WarningsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTextExtractions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTextExtractions_DocumentId",
                table: "DocumentTextExtractions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTextExtractions_DocumentId_ExtractionVersion",
                table: "DocumentTextExtractions",
                columns: new[] { "DocumentId", "ExtractionVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTextExtractions");
        }
    }
}
