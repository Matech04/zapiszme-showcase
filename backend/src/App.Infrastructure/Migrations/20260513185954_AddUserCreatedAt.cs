using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill istniejących userów timestampem migracji (CURRENT_TIMESTAMP),
            // żeby nie wpadły w grace-window cleanupu zaraz po deployu.
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailConfirmed_CreatedAt",
                table: "Users",
                columns: new[] { "EmailConfirmed", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_EmailConfirmed_CreatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "Users");
        }
    }
}
