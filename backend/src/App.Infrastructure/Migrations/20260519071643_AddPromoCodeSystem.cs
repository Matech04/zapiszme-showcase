using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCodeSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subscription_active_promo_code_redemption_id",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promo_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    discount_type = table.Column<int>(type: "integer", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    duration_months = table.Column<int>(type: "integer", nullable: true),
                    max_total_uses = table.Column<int>(type: "integer", nullable: true),
                    max_uses_per_tenant = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    current_uses = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applies_to = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promo_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promo_code_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promo_code_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    discount_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promo_code_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_promo_code_redemptions_Tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_promo_code_redemptions_promo_codes_promo_code_id",
                        column: x => x.promo_code_id,
                        principalTable: "promo_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_promo_code_redemptions_promo_code_id",
                table: "promo_code_redemptions",
                column: "promo_code_id");

            migrationBuilder.CreateIndex(
                name: "IX_promo_code_redemptions_tenant_id_is_active",
                table: "promo_code_redemptions",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_promo_codes_code",
                table: "promo_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promo_codes_is_active",
                table: "promo_codes",
                column: "is_active");

            // FK z Tenants.subscription_active_promo_code_redemption_id → promo_code_redemptions(id).
            // OnDelete: SetNull — jeśli redemption zostanie zhardkasowany, subskrypcja po prostu
            // przestaje mieć aplikowany rabat (cena wraca do bazowej). EF Core nie generuje tego FK
            // samodzielnie, bo Subscription jest owned entity bez nawigacji do PromoCodeRedemption.
            //
            // DEFERRABLE INITIALLY DEFERRED — Tenant↔PromoCodeRedemption to cykliczny FK
            // (Tenant.subscription_active_promo_code_redemption_id → redemption.Id,
            // redemption.tenant_id → Tenant.Id). Bez deferred FK, INSERT obu w jednej
            // transakcji rejestracji rzuca 23503 (FK violation). Postgres odracza check
            // do COMMIT, gdy oba wiersze są już zapisane.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Tenants""
                ADD CONSTRAINT fk_tenants_subscription_active_promo_redemption
                FOREIGN KEY (subscription_active_promo_code_redemption_id)
                REFERENCES promo_code_redemptions(id)
                ON DELETE SET NULL
                DEFERRABLE INITIALLY DEFERRED;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Tenants""
                DROP CONSTRAINT IF EXISTS fk_tenants_subscription_active_promo_redemption;
            ");

            migrationBuilder.DropTable(
                name: "promo_code_redemptions");

            migrationBuilder.DropTable(
                name: "promo_codes");

            migrationBuilder.DropColumn(
                name: "subscription_active_promo_code_redemption_id",
                table: "Tenants");
        }
    }
}
