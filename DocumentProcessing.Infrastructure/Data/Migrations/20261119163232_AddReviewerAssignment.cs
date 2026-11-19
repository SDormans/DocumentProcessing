using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewerAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAtUtc",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "Documents",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status_AssignedToUserId",
                table: "Documents",
                columns: new[] { "Status", "AssignedToUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Status_AssignedToUserId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Documents");
        }
    }
}
