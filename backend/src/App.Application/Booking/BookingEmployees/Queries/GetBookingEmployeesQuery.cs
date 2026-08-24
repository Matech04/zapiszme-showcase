using App.Application.Appointments;
using App.Application.Booking.BookingEmployees.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingEmployees.Queries;

/// <param name="ServiceIds">Usługi combo. Gdy niepuste — zwracani są pracownicy oferujący WSZYSTKIE
/// z nich (intersekcja), bo wizyta-combo jest realizowana przez jednego pracownika. Pusta/null →
/// WSZYSCY bookowalni pracownicy salonu — lejek klienta zaczyna od wyboru pracownika (przed usługą),
/// więc na starcie potrzebuje pełnej listy.</param>
public record GetBookingEmployeesQuery(IReadOnlyList<Guid>? ServiceIds = null) : IRequest<List<BookingEmployeeDto>>;

internal class GetBookingEmployeesQueryHandler : TenantHandler<GetBookingEmployeesQuery, List<BookingEmployeeDto>>
{
  /// <summary>
  /// Sufit okna skanu odznaki „ma terminy". Przy domyślnym horyzoncie (120 dni) nieaktywny.
  /// Chroni przed pełnym przemiataniem wieloletniego horyzontu na darmowym, anonimowym endpoincie.
  /// </summary>
  private const int MaxBadgeScanDays = 180;

  private readonly IApplicationDbContext _context;
  private readonly IEmployeeRepository _employeeRepository;

  public GetBookingEmployeesQueryHandler(
      IApplicationDbContext context,
      IEmployeeRepository employeeRepository,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
    _employeeRepository = employeeRepository;
  }

  public override async Task<List<BookingEmployeeDto>> Handle(GetBookingEmployeesQuery request, CancellationToken ct)
  {
    var query = _context.Employees
        .AsNoTracking()
        .Where(e => e.TenantId == TenantId && e.IsBookable);

    // Pusta lista usług → wszyscy bookowalni pracownicy (start lejka: pracownik przed usługą).
    // Niepusta → intersekcja: pracownik musi oferować KAŻDĄ z usług. Łańcuch EXISTS-ów (po jednym
    // per usługa) tłumaczy się na AND w SQL — w przeciwieństwie do `serviceIds.All(...)`, którego
    // EF nie przetłumaczy.
    if (request.ServiceIds is { Count: > 0 })
    {
      foreach (var serviceId in request.ServiceIds.Distinct())
      {
        var sid = serviceId;
        query = query.Where(e => e.Services.Any(s => s.ServiceId == sid));
      }
    }

    var employees = await query
        .Select(e => new { e.Id, e.FirstName, e.LastName })
        .ToListAsync(ct);

    if (employees.Count == 0)
    {
      return new List<BookingEmployeeDto>();
    }

    var scheduled = await FindWithUpcomingScheduleAsync(employees.Select(e => e.Id).ToList(), ct);

    return employees
        .Select(e => new BookingEmployeeDto(e.Id, e.FirstName, e.LastName, scheduled.Contains(e.Id)))
        .ToList();
  }

  /// <summary>
  /// Id pracowników mających choć jeden dzień roboczy w oknie rezerwacji. Pętla po dniach zrywa na
  /// pierwszym trafieniu, więc dla normalnie pracujących to 1–7 iteracji; pełne okno przechodzą
  /// tylko ci bez grafiku — czyli dokładnie ci, których i tak trzeba wykryć.
  /// </summary>
  private async Task<HashSet<Guid>> FindWithUpcomingScheduleAsync(
      IReadOnlyList<Guid> employeeIds, CancellationToken ct)
  {
    var tenant = await _context.Tenants.AsNoTracking()
        .Where(t => t.Id == TenantId)
        .Select(t => new { t.TimeZoneId, t.BookingHorizonDays })
        .FirstOrDefaultAsync(ct);

    var timeZoneId = tenant?.TimeZoneId ?? "Europe/Warsaw";
    var horizonDays = tenant?.BookingHorizonDays ?? 120;

    var from = AppointmentScheduleGuard.TodayInBusinessTimeZone(timeZoneId);
    // Okno = horyzont rezerwacji salonu, ale nie dalej niż MaxBadgeScanDays. Przy domyślnym
    // horyzoncie (120 dni) ten sufit nie zmienia nic — min(120, 180) = 120. Wchodzi w grę dopiero
    // przy salonie z bardzo długim horyzontem: Guard dopuszcza do 1826 dni, co dawałoby 1827 iteracji
    // NA PRACOWNIKA na anonimowym endpoincie (60/min/IP, produkcja 2 vCPU).
    //
    // Znane ograniczenie: miesiąc otwarty JAWNIE poza oknem (np. „grudzień już teraz, bo święta")
    // nie wpada do skanu, więc pracownik pracujący wyłącznie tam nie dostanie odznaki.
    // Badge wtedy nie dopowiada zamiast obiecywać — bezpieczniejszy kierunek pomyłki.
    var to = from.AddDays(Math.Min(horizonDays, MaxBadgeScanDays));

    var loaded = await _employeeRepository.GetManyForAvailabilityAsync(employeeIds, from, to, ct);

    var result = new HashSet<Guid>();
    foreach (var employee in loaded)
    {
      // Pracownik bez JAKIEGOKOLWIEK grafiku w oknie i tak nie dostanie odznaki — nie ma po co
      // przemiatać dnia po dniu. To właśnie ten przypadek przechodził wcześniej pełne okno:
      // `break` niżej ratuje tylko pracowników Z grafikiem. Override'y są już zawężone zakresem
      // przez repozytorium, grafiki filtrujemy po zakresie obowiązywania.
      var hasScheduleInWindow = employee.Overrides.Count > 0
          || employee.Schedules.Any(s =>
                s.IsActive && s.ActiveRange.StartDate <= to && s.ActiveRange.EndDate >= from);
      if (!hasScheduleInWindow)
      {
        continue;
      }

      for (var date = from; date <= to; date = date.AddDays(1))
      {
        // Grafik w zamkniętym miesiącu nie liczy się jako „ma terminy" — inaczej odznaka obiecuje
        // terminy, których klient nie może kliknąć, i lejek kończy się pustym kalendarzem.
        if (!employee.IsDateOpenForOnlineBooking(date, from, horizonDays))
        {
          continue;
        }

        var hasSchedule = employee.IsFixedForDate(date)
            ? employee.GetFixedStartTimes(date).Count > 0
            : employee.GetWorkingRanges(date).Count > 0;

        if (hasSchedule)
        {
          result.Add(employee.Id);
          break;
        }
      }
    }

    return result;
  }
}
