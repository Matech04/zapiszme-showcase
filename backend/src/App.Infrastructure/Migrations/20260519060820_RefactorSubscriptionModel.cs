using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Infrastructure.Migrations
{
    /// <summary>
    /// Refaktor modelu subskrypcji: usunięcie planu Free, dodanie Seats + IsFoundingMember,
    /// rename PaidUntil → CurrentPeriodEndsAt, plan → status.
    ///
    /// Data migration (zgodnie z decyzją produktową 1A):
    ///   * stary Free (1)  → Trial 30 dni od NOW()
    ///   * stary Trial (0) → Trial (zachowuje istniejący trialEndsAt)
    ///   * stary Pro (2)   → Active (currentPeriodEndsAt = stare paid_until)
    /// </summary>
    public partial class RefactorSubscriptionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Nowe kolumny — z defaultami żeby istniejące wiersze były spójne na początku.
            migrationBuilder.AddColumn<int>(
                name: "subscription_status",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "subscription_seats",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "subscription_is_founding_member",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subscription_current_period_ends_at",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            // 2) subscription_trial_ends_at musi stać się nullable (Active/PastDue/Canceled = null).
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "subscription_trial_ends_at",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            // 3) Data migration — konwersja istniejących wartości subscription_plan.
            //    Stare wartości plan: Trial=0, Free=1, Pro=2.
            //    Nowe wartości status: Trial=0, Active=1, PastDue=2, Canceled=3.
            migrationBuilder.Sql(@"
                UPDATE ""Tenants""
                SET
                    subscription_status =
                        CASE subscription_plan
                            WHEN 1 THEN 0  -- Free → Trial (decyzja 1A: 30-dniowy grace)
                            WHEN 2 THEN 1  -- Pro  → Active
                            ELSE 0         -- Trial → Trial
                        END,
                    subscription_trial_ends_at =
                        CASE subscription_plan
                            WHEN 1 THEN NOW() + INTERVAL '30 days'  -- Free → świeży 30-dniowy trial
                            WHEN 2 THEN NULL                         -- Pro  → brak trialu
                            ELSE subscription_trial_ends_at          -- Trial → zachowaj istniejący
                        END,
                    subscription_current_period_ends_at =
                        CASE subscription_plan
                            WHEN 2 THEN subscription_paid_until  -- Pro → przenieś okres rozliczeniowy
                            ELSE NULL
                        END,
                    subscription_seats = 1,
                    subscription_is_founding_member = false;
            ");

            // 4) Stare kolumny już niepotrzebne.
            migrationBuilder.DropColumn(name: "subscription_plan", table: "Tenants");
            migrationBuilder.DropColumn(name: "subscription_paid_until", table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down — best-effort: przywróć stare kolumny i mapowanie odwrotne.
            // UWAGA: Free → po migracji jest Trial; przywrócenie traci tę informację (każdy konwertowany
            //        Free wraca jako Trial, nie jako Free). To akceptowalne, bo decyzja 1A była jednokierunkowa.

            migrationBuilder.AddColumn<int>(
                name: "subscription_plan",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subscription_paid_until",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Tenants""
                SET
                    subscription_plan =
                        CASE subscription_status
                            WHEN 1 THEN 2  -- Active → Pro
                            ELSE 0         -- Trial / PastDue / Canceled → Trial
                        END,
                    subscription_paid_until = subscription_current_period_ends_at;
            ");

            migrationBuilder.DropColumn(name: "subscription_status", table: "Tenants");
            migrationBuilder.DropColumn(name: "subscription_seats", table: "Tenants");
            migrationBuilder.DropColumn(name: "subscription_is_founding_member", table: "Tenants");
            migrationBuilder.DropColumn(name: "subscription_current_period_ends_at", table: "Tenants");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "subscription_trial_ends_at",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
