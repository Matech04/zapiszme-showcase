namespace App.Application.Tenants.Dtos;

public record TenantAdminDto(
    Guid Id,
    string Name,
    string Slug,
    string TimeZoneId,
    string Currency,
    string Status,
    string EffectiveStatus,
    int Seats,
    bool IsFoundingMember,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    bool IsTrialActive,
    int DaysRemainingInTrial,
    int MonthlyPriceInGrosze,
    int MonthlySmsAllowance,
    int? MonthlySmsHardCap,
    int EffectiveMonthlySmsCap
);
