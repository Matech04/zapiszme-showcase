using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SelfServiceOtps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfServiceOtps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    PhoneE164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SessionToken = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfServiceOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfServiceOtps_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfServiceOtps_ExpiresAtUtc",
                table: "SelfServiceOtps",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SelfServiceOtps_SessionToken",
                table: "SelfServiceOtps",
                column: "SessionToken");

            migrationBuilder.CreateIndex(
                name: "IX_SelfServiceOtps_TenantId_Email",
                table: "SelfServiceOtps",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfServiceOtps_TenantId_PhoneE164",
                table: "SelfServiceOtps",
                columns: new[] { "TenantId", "PhoneE164" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfServiceOtps");
        }
    }
}
