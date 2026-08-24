using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;

namespace App.Application.Appointments;

/// <summary>
/// Czysta logika planowania zamiany dwóch wizyt — współdzielona przez komendę zamiany
/// i zapytanie podglądu. Wylicza docelowy slot/usługę/cenę każdej wizyty i sprawdza, czy
/// stan docelowy mieści się w kalendarzach obu pracowników.
/// </summary>
internal static class AppointmentSwapPlanner
{
  internal sealed record SwapTarget(
    Appointment Appointment,
    Employee Employee,
    Guid ServiceId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    Money Price)
  {
    public TimeRange Range => new(StartTime, EndTime);
  }

  /// <summary>
  /// Plan harmonizacji: która wizyta jest dłuższa i jaką (krótszą) usługę ma przyjąć, by obie
  /// miały tę samą długość. Null, gdy długości są równe (harmonizacja nie jest potrzebna).
  /// </summary>
  internal sealed record HarmonizationPlan(Appointment LongerAppointment, Service LongerService, Service ShorterService);

  public static int DurationMinutes(Appointment appointment)
    => (int)(appointment.EndTime - appointment.StartTime).TotalMinutes;

  public static HarmonizationPlan? PlanHarmonization(Appointment first, Service firstService, Appointment second, Service secondService)
  {
    var firstDuration = DurationMinutes(first);
    var secondDuration = DurationMinutes(second);
    if (firstDuration == secondDuration)
    {
      return null;
    }

    return firstDuration > secondDuration
      ? new HarmonizationPlan(first, firstService, secondService)
      : new HarmonizationPlan(second, secondService, firstService);
  }

  /// <summary>
  /// Buduje stan docelowy dla wizyty przeniesionej do slotu (<paramref name="destDate"/>,
  /// <paramref name="destStart"/>) z usługą <paramref name="service"/>. Czas zakończenia i cenę
  /// liczymy dla pracownika tej wizyty (uwzględnia custom duration/price). Rzuca
  /// <see cref="App.Domain.Exceptions.EmployeeServiceMissingException"/>, jeśli pracownik nie ma
  /// przypisanej usługi — sygnał, że harmonizacja nie jest możliwa.
  /// </summary>
  public static SwapTarget BuildTarget(Appointment moving, Employee employee, Service service, DateOnly destDate, TimeOnly destStart)
  {
    var endTime = employee.CalculateEndTime(destStart, service.Id, service.DurationInMinutes);
    var price = employee.CalculateTotalPrice(service.Id, service.Price);
    // Świeża instancja Money per wizyta — owned entity nie może być współdzielona między
    // właścicielami (EF deduplikuje po referencji i rzuca na kluczu owned-FK).
    return new SwapTarget(moving, employee, service.Id, destDate, destStart, endTime, new Money(price.Amount, price.Currency));
  }

  /// <summary>
  /// Czy oba docelowe sloty mieszczą się w kalendarzach pracowników. Kolizje liczymy ignorując
  /// obie zamieniane wizyty (zwalniają swoje sloty), a gdy trafiają na tego samego pracownika
  /// w ten sam dzień — dodatkowo sprawdzamy, że nie nachodzą na siebie nawzajem.
  /// </summary>
  public static async Task<bool> FitsAsync(IAppointmentService appointmentService, Guid tenantId, SwapTarget first, SwapTarget second)
  {
    var ignore = new[] { first.Appointment.Id, second.Appointment.Id };

    if (!await appointmentService.IsAvailableAsync(first.Employee, first.Range, first.Date, tenantId, ignore))
    {
      return false;
    }

    if (!await appointmentService.IsAvailableAsync(second.Employee, second.Range, second.Date, tenantId, ignore))
    {
      return false;
    }

    if (first.Employee.Id == second.Employee.Id && first.Date == second.Date && first.Range.OverlapsWith(second.Range))
    {
      return false;
    }

    return true;
  }
}
