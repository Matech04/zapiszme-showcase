using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Queries.GetEmployeeLeaves;

/// <summary>
/// Urlopy/nieobecności pracownika. Dostęp bramkuje `IStaffAccessPolicy.EnsureCanReadEmployeeCalendarDataAsync`
/// (self, Owner/Manager/Admin, Kiosk, albo pracownik w salonie z `StaffCalendarVisibilityPolicy` ≥
/// TeamReadOnly) — bez tego kalendarz zespołu pokazywałby „Dzień wolny" u kolegów i pozwalał umówić
/// wizytę na czyjś urlop.
///
/// UWAGA: kto MOŻE czytać, nie znaczy, że widzi WSZYSTKO. Polityka rozstrzyga dostęp do zasobu, ten
/// handler — zakres pola. `AbsenceType.SickLeave` to dana o zdrowiu (art. 9 RODO): kolega z zespołu
/// i współdzielony terminal „Recepcja" dostają zakres dat i `BlocksDay`, a powód nieobecności widzi
/// sam pracownik oraz Owner/Manager/Admin.
/// </summary>
public record GetEmployeeLeavesQuery(Guid EmployeeId) : IRequest<List<EmployeeLeaveDto>>;

internal class GetEmployeeLeavesQueryHandler : TenantHandler<GetEmployeeLeavesQuery, List<EmployeeLeaveDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;
  private readonly ICurrentUserAccessor _currentUser;

  public GetEmployeeLeavesQueryHandler(
      IApplicationDbContext context,
      IStaffAccessPolicy access,
      ICurrentUserAccessor currentUser,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _access = access;
    _currentUser = currentUser;
  }

  public override async Task<List<EmployeeLeaveDto>> Handle(GetEmployeeLeavesQuery request, CancellationToken ct)
  {
    await _access.EnsureCanReadEmployeeCalendarDataAsync(request.EmployeeId, ct);

    // WYDAJNOŚĆ — nie zamieniaj tego na materializację encji `Employee` (z `Include(e => e.Leaves)`
    // albo bez). Employee ma cztery kolekcje owned, dwie zagnieżdżone na 3 poziomy, a EF ładuje je
    // ZAWSZE z rodzicem — `Include` niczego nie zawęża. Zmierzony SQL poprzedniej wersji łączył
    // DZIEWIĘĆ tabel w jednym SELECT (EmployeeLeaves × ScheduleOverrides·Breaks·WorkRanges ×
    // EmployeeSchedules·ScheduleDays·Breaks·WorkRanges × EmployeeServices) = iloczyn kartezjański,
    // po to żeby oddać kilka urlopów. Projekcja schodzi do dwóch tabel i czterech kolumn.
    //
    // `FirstOrDefaultAsync` na projekcji listy rozróżnia oba przypadki: brak pracownika → null,
    // pracownik bez urlopów → pusta lista. Dlatego jawne porównanie z null, a nie `?? throw`.
    var leaves = await _context.Employees
        .AsNoTracking()
        .Where(e => e.Id == request.EmployeeId && e.TenantId == TenantId)
        .Select(e => e.Leaves
            .OrderBy(l => l.DateRange.StartDate)
            .Select(l => new EmployeeLeaveDto(
              l.Id,
              l.DateRange.StartDate,
              l.DateRange.EndDate,
              l.AbsenceType,
              l.AbsenceStatus,
              // Czy nieobecność zdejmuje pas pracy. `SpecialDay` nie zdejmuje — to znacznik
              // opisowy, nie urlop. Liczone tutaj, bo przy zamaskowanym typie front nie ma
              // z czego tego wywnioskować (patrz `EmployeeLeaveDto`).
              l.AbsenceType == AbsenceType.Vacation || l.AbsenceType == AbsenceType.SickLeave))
            .ToList())
        .FirstOrDefaultAsync(ct);

    if (leaves is null)
    {
      throw new NotFoundException(nameof(Employee), request.EmployeeId);
    }

    // Maskowanie PO materializacji, nie w projekcji — SQL zostaje bajt w bajt taki jak był
    // (dwie tabele, patrz notka wyżej), a warunek jest zwykłym `if`, nie wyrażeniem do
    // przetłumaczenia. Kiosk NIE ma `CanManageOtherEmployees`, więc jako współdzielony terminal
    // recepcji dostaje sam zakres dat — tak jak koledzy.
    var maySeeReason =
      _currentUser.CanManageOtherEmployees || _currentUser.CallerEmployeeId == request.EmployeeId;

    return maySeeReason
      ? leaves
      : leaves.Select(l => l with { AbsenceType = null }).ToList();
  }
}
