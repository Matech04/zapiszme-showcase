using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_promo_code",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "industry",
                table: "Tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "onboarding_completed_at",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: każdy ISTNIEJĄCY salon jest już w pełni skonfigurowany (żywy tenant sprzed
            // kreatora). Bez tego twardy guard /admin/** (onboarding_completed_at == null → /setup)
            // uwięziłby wszystkie obecne salony po deployu. Nowe tenanty startują z NULL i dostają
            // znacznik dopiero po ukończeniu kreatora (CompleteOnboarding) / od razu przy tworzeniu
            // przez admina i demo.
            migrationBuilder.Sql(
                "UPDATE \"Tenants\" SET onboarding_completed_at = now() WHERE onboarding_completed_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_promo_code",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "industry",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "onboarding_completed_at",
                table: "Tenants");
        }
    }
}
