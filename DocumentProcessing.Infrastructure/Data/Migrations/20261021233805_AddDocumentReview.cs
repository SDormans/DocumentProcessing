using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastProcessedAtUtc",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAiOutputJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CorrectedValuesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ApprovedValuesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewDecision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReviews_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceExtractionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtractionVersion = table.Column<int>(type: "integer", nullable: false),
                    ExtractionMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCharacterCount = table.Column<int>(type: "integer", nullable: false),
                    RawAiResponse = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LineItemsJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierNameHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InvoiceNumberHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvoiceDateHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDateHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CurrencyHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    SubtotalHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TaxAmountHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TotalAmountHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    Lines = table.Column<string>(type: "text", nullable: true),
                    LinesHasValue = table.Column<bool>(type: "boolean", nullable: false),
                    WarningsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ExtractedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceExtractionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceExtractionResults_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FieldName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OldValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewAuditEntries_DocumentReviews_DocumentReviewId",
                        column: x => x.DocumentReviewId,
                        principalTable: "DocumentReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviews_DocumentId",
                table: "DocumentReviews",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviews_Status",
                table: "DocumentReviews",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceExtractionResults_DocumentId",
                table: "InvoiceExtractionResults",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceExtractionResults_DocumentId_ExtractionVersion",
                table: "InvoiceExtractionResults",
                columns: new[] { "DocumentId", "ExtractionVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewAuditEntries_CreatedAtUtc",
                table: "ReviewAuditEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewAuditEntries_DocumentReviewId",
                table: "ReviewAuditEntries",
                column: "DocumentReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceExtractionResults");

            migrationBuilder.DropTable(
                name: "ReviewAuditEntries");

            migrationBuilder.DropTable(
                name: "DocumentReviews");

            migrationBuilder.DropColumn(
                name: "LastProcessedAtUtc",
                table: "Documents");
        }
    }
}
