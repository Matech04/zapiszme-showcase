using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonationSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "impersonation_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_read_only = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impersonation_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_sessions_admin_user_id",
                table: "impersonation_sessions",
                column: "admin_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_sessions_ended_at_utc_expires_at_utc",
                table: "impersonation_sessions",
                columns: new[] { "ended_at_utc", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_sessions_target_tenant_id",
                table: "impersonation_sessions",
                column: "target_tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "impersonation_sessions");
        }
    }
}
