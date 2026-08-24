using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIsDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "demo_created_at_utc",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_demo",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_is_demo_demo_created_at_utc",
                table: "Tenants",
                columns: new[] { "is_demo", "demo_created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenants_is_demo_demo_created_at_utc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "demo_created_at_utc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "is_demo",
                table: "Tenants");
        }
    }
}
