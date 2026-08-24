using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetScheduleOverrides;

public record GetScheduleOverridesQuery(Guid EmployeeId) : IRequest<List<ScheduleOverrideDto>>;

internal class GetScheduleOverridesQueryHandler : TenantHandler<GetScheduleOverridesQuery, List<ScheduleOverrideDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetScheduleOverridesQueryHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }

  public override async Task<List<ScheduleOverrideDto>> Handle(GetScheduleOverridesQuery request, CancellationToken ct)
  {
    // Godziny pracy / dni specjalne / usługi kolegi — odczyt szerszy niż profil, ale ograniczony
    // do tego salonu i do polityki widoczności kalendarza.
    await _access.EnsureCanReadEmployeeCalendarDataAsync(request.EmployeeId, ct);

    // WYDAJNOŚĆ — nie wracaj do materializacji encji `Employee`. Kolekcje owned jadą z rodzicem
    // ZAWSZE (`Include` niczego nie zawęża), więc poprzednia wersja ciągnęła cały agregat:
    // zmierzony SQL łączył dziewięć tabel w jednym SELECT, żeby oddać listę nadpisań grafiku.
    //
    // UWAGA na ścieżkę dostępu: `o.WorkRanges`, `o.Breaks`, `o.FixedStartTimes` i `o.IsFixed` na
    // ScheduleOverride to właściwości WYLICZANE, delegujące do `ScheduleDay` — nie mają mapowania
    // i w projekcji nie przetłumaczą się na SQL. Dlatego sięgamy przez `o.ScheduleDay.*`, czyli
    // realnie zmapowane nawigacje. `IsFixed` (= `_fixedStartTimes.Count > 0`) wyrażamy wprost
    // jako warunek na kolekcji.
    var overrides = await _context.Employees
        .AsNoTracking()
        .Where(e => e.Id == request.EmployeeId && e.TenantId == TenantId)
        .Select(e => e.Overrides
            .OrderBy(o => o.Date)
            .Select(o => new ScheduleOverrideDto(
                o.Date,
                o.ScheduleDay.FixedStartTimes.Count > 0
                  ? SlotGenerationMode.FixedStartTimes
                  : SlotGenerationMode.Grid,
                o.ScheduleDay.WorkRanges.Select(r => new TimeRangeDto(r.StartTime, r.EndTime)).ToList(),
                o.ScheduleDay.Breaks.Select(b => new TimeRangeDto(b.StartTime, b.EndTime)).ToList(),
                o.ScheduleDay.FixedStartTimes.ToList()))
            .ToList())
        .FirstOrDefaultAsync(ct);

    return overrides ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
  }
}