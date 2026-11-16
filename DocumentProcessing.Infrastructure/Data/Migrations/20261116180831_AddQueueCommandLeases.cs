using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueCommandLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueCommands_DocumentId",
                table: "QueueCommands");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "QueueCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "QueueCommands",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueCommands_DocumentId_Active",
                table: "QueueCommands",
                column: "DocumentId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCommands_Status_NextAttemptAtUtc",
                table: "QueueCommands",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueCommands_DocumentId_Active",
                table: "QueueCommands");

            migrationBuilder.DropIndex(
                name: "IX_QueueCommands_Status_NextAttemptAtUtc",
                table: "QueueCommands");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "QueueCommands");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "QueueCommands");

            migrationBuilder.CreateIndex(
                name: "IX_QueueCommands_DocumentId",
                table: "QueueCommands",
                column: "DocumentId");
        }
    }
}
