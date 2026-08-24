using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPhoneNumberSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumberSearch",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Backfill istniejących klientów: same cyfry z E164 (np. "+48509123456" -> "48509123456"),
            // tak samo jak PhoneNumber.SearchValue w domenie. Bez tego stare rekordy nie znalazłyby się
            // w wyszukiwarce po numerze, dopóki ktoś ich nie zedytuje.
            migrationBuilder.Sql(
                "UPDATE \"Customers\" SET \"PhoneNumberSearch\" = regexp_replace(\"PhoneNumber\", '[^0-9]', '', 'g') WHERE \"PhoneNumber\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_PhoneNumberSearch",
                table: "Customers",
                columns: new[] { "TenantId", "PhoneNumberSearch" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_PhoneNumberSearch",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PhoneNumberSearch",
                table: "Customers");
        }
    }
}
