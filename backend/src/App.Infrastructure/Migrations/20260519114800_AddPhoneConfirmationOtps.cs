using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneConfirmationOtps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneConfirmationOtps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptsCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneConfirmationOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneConfirmationOtps_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_phone_confirmation_otps_active_per_user",
                table: "PhoneConfirmationOtps",
                column: "UserId",
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneConfirmationOtps_ExpiresAt",
                table: "PhoneConfirmationOtps",
                column: "ExpiresAt");

            // Backfill: istniejący użytkownicy z potwierdzonym emailem dostają PhoneNumberConfirmed=true,
            // żeby nowe gate (login wymaga obu) nie wykluczyło ich z logowania po wdrożeniu.
            // Nowi rejestrujący się przejdą pełen flow (RegisterOwner → ConfirmEmail → SMS OTP → ConfirmPhone).
            // Uwaga: UserConfiguration.ToTable("Users") — Identity AspNetUsers jest tu renamowana.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"PhoneNumberConfirmed\" = TRUE WHERE \"EmailConfirmed\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneConfirmationOtps");
        }
    }
}
