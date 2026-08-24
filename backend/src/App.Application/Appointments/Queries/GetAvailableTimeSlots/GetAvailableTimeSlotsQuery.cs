using App.Application.Appointments;
using App.Application.Appointments.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetAvailableTimeSlots;

/// <param name="EnforcePublicVisibility">
/// Egzekwuje widoczność terminu dla KLIENTA (horyzont rezerwacji + publikacja miesiąca).
/// Ustawiane wyłącznie przez publiczny booking — personel w panelu musi móc wpisać wizytę
/// dowolnie daleko w przód i w miesiącu, który dla klientów jest jeszcze zamknięty.
/// </param>
public record GetAvailableTimeSlotsQuery(DateOnly Date, Guid EmployeeId, IReadOnlyList<Guid> ServiceIds, bool SkipGapFilling = false, bool EnforceStrictGapFilter = false, bool EnforcePublicVisibility = false) : IRequest<List<AppointmentSlotDto>>;

internal class GetAvailableTimeSlotsHandler : TenantHandler<GetAvailableTimeSlotsQuery, List<AppointmentSlotDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IAppointmentService _appointementService;

  public GetAvailableTimeSlotsHandler(IApplicationDbContext context,
  IEmployeeRepository employeeRepository,
  ICurrentTenantService currentTenantService,
  IAppointmentService appointmentService)
      : base(currentTenantService)
  {
    _context = context;
    _employeeRepository = employeeRepository;
    _appointementService = appointmentService;
  }

  public override async Task<List<AppointmentSlotDto>> Handle(GetAvailableTimeSlotsQuery request, CancellationToken ct)
  {
    var tenant = await _context.Tenants.AsNoTracking()
      .Where(t => t.Id == TenantId)
      .FirstOrDefaultAsync(ct);

    var tenantTimeZoneId = tenant?.TimeZoneId ?? "Europe/Warsaw";

    var todayBiz = AppointmentScheduleGuard.TodayInBusinessTimeZone(tenantTimeZoneId);
    if (request.Date < todayBiz)
    {
      return new List<AppointmentSlotDto>();
    }

    // Dostępność liczona dla pojedynczego dnia — ładujemy override/urlopy tylko z tego dnia.
    var employee = await _employeeRepository.GetForAvailabilityAsync(request.EmployeeId, request.Date, request.Date, ct)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    // Górna granica widoczności dla klienta. Świadomie PO załadowaniu pracownika, bo o otwarciu
    // miesiąca decyduje jego własny wiersz publikacji, a dopiero w jego braku horyzont salonu.
    if (request.EnforcePublicVisibility &&
        !employee.IsDateOpenForOnlineBooking(request.Date, todayBiz, tenant?.BookingHorizonDays ?? 120))
    {
      return new List<AppointmentSlotDto>();
    }

    // Anonimowy hold OTP (AwaitingOtp) z WYGASŁYM lease nie blokuje już slotu — inaczej slot byłby
    // martwy do następnego przebiegu joba sprzątającego (interwał 1 min) = wektor DoS dostępności.
    // Traktujemy go jak wolny od razu. Booked/Pending (panel lub manual po OTP) blokują zawsze,
    // nawet ze stale lease — dlatego wykluczamy WYŁĄCZNIE AwaitingOtp.
    var nowUtc = DateTime.UtcNow;

    // Fetch ze Statusem bo collision-check potrzebuje WSZYSTKICH aktywnych (włącznie z
    // tymczasowymi AwaitingOtp/Pending), natomiast gap-filling MUSI pomijać tylko
    // squatter-y trzymające slot z OTP-em (AwaitingOtp) — wygasają przez HoldLease.
    var employeeAppointmentsRaw = await _context.Appointments.AsNoTracking()
        .Where(a => a.TenantId == TenantId &&
        a.Date == request.Date &&
        a.EmployeeId == request.EmployeeId &&
        a.Status != AppointmentStatus.Canceled &&
        !(a.Status == AppointmentStatus.AwaitingOtp && a.Lease!.ExpiryTimeUtc < nowUtc))
        .Select(a => new { a.StartTime, a.EndTime, a.Status }).ToListAsync(ct);

    var employeeAppointments = employeeAppointmentsRaw
      .Select(a => new TimeRange(a.StartTime, a.EndTime))
      .ToList();

    // Gap-filling: uwzględniamy Pending/Booked/InProgress/Completed. AwaitingOtp wyklucza
    // się, bo to anonimowy hold OTP z public-bookingu (auto-wygasa). Pending w panelu
    // to "klient się umówił, salon zatwierdza ręcznie" — wizyta de facto zarezerwowana,
    // powinna wpływać na sugestie wypełniania luk.
    var confirmedAppointments = employeeAppointmentsRaw
      .Where(a => a.Status == AppointmentStatus.Pending
               || a.Status == AppointmentStatus.Booked
               || a.Status == AppointmentStatus.InProgress
               || a.Status == AppointmentStatus.Completed)
      .Select(a => new TimeRange(a.StartTime, a.EndTime))
      .ToList();

    // Czas combo = suma czasów wybranych usług (z uwzględnieniem per-pracownik override).
    // Jedno zapytanie zamiast K round-tripów (combo); brak którejkolwiek usługi → NotFound.
    var serviceDuration = await ComboDurationResolver.ResolveAsync(
      _context, employee, request.ServiceIds, ct);

    var appointmentSlotStepMinutes = tenant?.AppointmentSlotStepMinutes ?? 15;

    // Tryb per-dzień: override na ten dzień może być w innym trybie niż globalny tryb pracownika.
    var isFixed = employee.IsFixedForDate(request.Date);

    var rawSlotsList = isFixed
      ? _appointementService.EmployeeFixedSlotsList(
          employee.GetFixedStartTimes(request.Date),
          employeeAppointments,
          employee,
          serviceDuration)
      : _appointementService.EmployeeAvailableSlotsList(
          employee.GetWorkingRanges(request.Date),
          employeeAppointments,
          employee,
          serviceDuration,
          appointmentSlotStepMinutes == 0 ? 15 : appointmentSlotStepMinutes);

    if (request.Date == todayBiz)
    {
      rawSlotsList = rawSlotsList
        .Where(t => !AppointmentScheduleGuard.IsStartInPast(tenantTimeZoneId, request.Date, t))
        .ToList();
    }

    // Tryb stałych slotów: gęstość ustala operatorka — gap-filling pomijamy (null = passthrough wszystkich slotów).
    var effectiveSettings = isFixed
      ? null
      : StaffGapFillingSettingsResolver.Resolve(
          tenant?.GapFillingSettings,
          request.SkipGapFilling,
          request.EnforceStrictGapFilter);

    var result = GapFillingSlotFilter.Apply(
      rawSlotsList,
      confirmedAppointments,
      serviceDuration,
      appointmentSlotStepMinutes == 0 ? 15 : appointmentSlotStepMinutes,
      effectiveSettings);

    return result;
  }
}
