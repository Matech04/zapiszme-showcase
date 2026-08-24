using App.Application.Common.Interfaces;
using App.Application.Tenants.Dtos;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Queries.GetTenantsAdmin;

public record GetTenantsAdminQuery : IRequest<List<TenantAdminDto>>;

public class GetTenantsAdminQueryHandler : IRequestHandler<GetTenantsAdminQuery, List<TenantAdminDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTenantsAdminQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    // Backstop przeciw nieograniczonemu materializowaniu listy w miarę wzrostu liczby salonów.
    // Cap dużo powyżej realnej skali — przy zbliżaniu się do niego wprowadzić właściwą paginację.
    private const int MaxResults = 1000;

    public async Task<List<TenantAdminDto>> Handle(GetTenantsAdminQuery request, CancellationToken ct)
    {
        var tenants = await _context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Take(MaxResults)
            .ToListAsync(ct);
        return tenants.Select(ToDto).ToList();
    }

    private static TenantAdminDto ToDto(Tenant t)
    {
        var sub = t.Subscription;
        return new TenantAdminDto(
            t.Id,
            t.Name,
            t.Slug,
            t.TimeZoneId,
            t.Currency,
            sub.Status.ToString(),
            sub.EffectiveStatus.ToString(),
            sub.Seats,
            sub.IsFoundingMember,
            sub.TrialEndsAt,
            sub.CurrentPeriodEndsAt,
            sub.IsTrialActive,
            sub.DaysRemainingInTrial,
            sub.MonthlyPriceInGrosze,
            sub.MonthlySmsAllowance,
            sub.MonthlySmsHardCap,
            sub.EffectiveMonthlySmsCap
        );
    }
}
