using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetMonthPublications;

public record GetMonthPublicationsQuery(Guid EmployeeId) : IRequest<List<MonthPublicationDto>>;

internal class GetMonthPublicationsQueryHandler : TenantHandler<GetMonthPublicationsQuery, List<MonthPublicationDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;

  public GetMonthPublicationsQueryHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
  }

  public override async Task<List<MonthPublicationDto>> Handle(GetMonthPublicationsQuery request, CancellationToken ct)
  {
    // Publikacja miesiąca to dana kalendarza zespołu — ten sam zasięg odczytu co grafik, dni
    // specjalne i urlopy (kontroler wołał tu `CanReadEmployeeCalendarDataAsync`).
    await _access.EnsureCanReadEmployeeCalendarDataAsync(request.EmployeeId, ct);

    // WYDAJNOŚĆ — nie materializuj encji `Employee`. Kolekcje owned jadą z rodzicem ZAWSZE
    // (`Include` niczego nie zawęża), więc odczyt kilku wierszy publikacji ciągnąłby cały agregat
    // pracownika: cztery kolekcje owned, dwie zagnieżdżone na trzy poziomy. Ten sam powód i ten sam
    // kształt co w GetScheduleOverrides / GetEmployeeSchedules / GetEmployeeLeaves.
    var publications = await _context.Employees
        .AsNoTracking()
        .Where(e => e.Id == request.EmployeeId && e.TenantId == TenantId)
        .Select(e => e.MonthPublications
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .Select(p => new MonthPublicationDto(p.Year, p.Month, p.OpensOn))
            .ToList())
        .FirstOrDefaultAsync(ct);

    return publications ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
  }
}
