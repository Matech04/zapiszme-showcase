using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceIsAddonAndServiceAddons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_addon",
                table: "Services",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ServiceAddons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    MainServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    addon_service_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAddons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceAddons_Services_MainServiceId",
                        column: x => x.MainServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceAddons_Services_addon_service_id",
                        column: x => x.addon_service_id,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceAddons_Tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAddons_addon_service_id",
                table: "ServiceAddons",
                column: "addon_service_id");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAddons_MainServiceId",
                table: "ServiceAddons",
                column: "MainServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAddons_tenant_id_MainServiceId_addon_service_id",
                table: "ServiceAddons",
                columns: new[] { "tenant_id", "MainServiceId", "addon_service_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceAddons");

            migrationBuilder.DropColumn(
                name: "is_addon",
                table: "Services");
        }
    }
}
