using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiModelType",
                table: "InvoiceExtractionResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "InvoiceExtractionResults",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiModelType",
                table: "Documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreferredAiModelType",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiModelType",
                table: "InvoiceExtractionResults");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "InvoiceExtractionResults");

            migrationBuilder.DropColumn(
                name: "AiModelType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PreferredAiModelType",
                table: "AspNetUsers");
        }
    }
}
