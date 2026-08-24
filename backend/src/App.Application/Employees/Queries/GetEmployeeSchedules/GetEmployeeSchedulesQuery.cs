using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetEmployeeSchedules;

public record GetEmployeeSchedulesQuery(Guid EmployeeId) : IRequest<List<EmployeeScheduleDto>>;

internal class GetEmployeeSchedulesQueryHandler : TenantHandler<GetEmployeeSchedulesQuery, List<EmployeeScheduleDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetEmployeeSchedulesQueryHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }

  public override async Task<List<EmployeeScheduleDto>> Handle(GetEmployeeSchedulesQuery request, CancellationToken ct)
  {
    // Godziny pracy / dni specjalne / usługi kolegi — odczyt szerszy niż profil, ale ograniczony
    // do tego salonu i do polityki widoczności kalendarza.
    await _access.EnsureCanReadEmployeeCalendarDataAsync(request.EmployeeId, ct);

    // WYDAJNOŚĆ — nie wracaj do materializacji encji `Employee` (z `Include`/`ThenInclude` czy bez).
    // Kolekcje owned jadą z rodzicem ZAWSZE, więc poprzednia wersja ciągnęła cały agregat: zmierzony
    // SQL łączył dziewięć tabel w jednym SELECT (grafiki × dni × zakresy × przerwy × nadpisania ×
    // ich zakresy i przerwy × urlopy × usługi pracownika) = iloczyn kartezjański. Projekcja
    // ogranicza zapytanie do gałęzi grafików.
    //
    // Iloczyn dni × zakresy × przerwy W OBRĘBIE grafiku zostaje — jest mały i nieunikniony bez
    // `AsSplitQuery` (pakiet .Relational, którego ta warstwa celowo nie referencuje).
    var schedules = await _context.Employees
      .AsNoTracking()
      .Where(e => e.Id == request.EmployeeId && e.TenantId == TenantId)
      .Select(e => e.Schedules
      .OrderBy(s => s.ActiveRange.StartDate)
      .Select(s => new EmployeeScheduleDto(
        s.ActiveRange.StartDate,
        s.ActiveRange.EndDate,
        s.NumberOfCycles,
        s.ScheduleDays
          .Where(d => d.CycleIndex.HasValue)
          .OrderBy(d => d.CycleIndex)
          .Select(d => new EmployeeScheduleDayDto(
            d.CycleIndex!.Value,
            d.WorkRanges.Select(r => new TimeRangeDto(r.StartTime, r.EndTime)).ToList(),
            d.Breaks.Select(b => new TimeRangeDto(b.StartTime, b.EndTime)).ToList(),
            d.FixedStartTimes.ToList()))
          .ToList(),
        s.Id,
        // Tryb wyprowadzamy z DNI TEGO grafiku (dzień „fixed" ma godziny startu), a nie z globalnego
        // trybu pracownika — inaczej przy kilku grafikach o różnych trybach wszystkie dostawałyby tryb
        // ostatnio zapisanego, więc grafik statyczny renderowałby się jako pusty Grid.
        s.ScheduleDays.Any(d => d.FixedStartTimes.Count > 0)
          ? SlotGenerationMode.FixedStartTimes
          : SlotGenerationMode.Grid,
        s.IsActive))
      .ToList())
      .FirstOrDefaultAsync(ct);

    return schedules ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
  }
}
