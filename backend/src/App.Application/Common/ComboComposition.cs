using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;

namespace App.Application.Common;

/// <summary>
/// Reguły składu combo egzekwowane autorytatywnie na backendzie (panel i booking). Sprawdza
/// liczbę, unikalność i — kluczowe — zakaz dwóch usług z tej samej (niepustej) grupy wariantów
/// (<see cref="Service.ComboGroup"/>). Wołane PRZED zbudowaniem pozycji wizyty.
/// </summary>
public static class ComboCompositionValidator
{
  public static void EnsureValidComposition(IReadOnlyList<Service> services)
  {
    if (services is null || services.Count == 0)
    {
      throw new AppointmentBookingRuleException("Wizyta musi mieć co najmniej jedną usługę.", ErrorCodes.AppointmentNoServices);
    }

    if (services.Count > Appointment.MaxServices)
    {
      throw new AppointmentBookingRuleException($"Wizyta może mieć maksymalnie {Appointment.MaxServices} usług.", ErrorCodes.AppointmentTooManyServices);
    }

    if (services.Select(s => s.Id).Distinct().Count() != services.Count)
    {
      throw new AppointmentBookingRuleException("Usługi w wizycie nie mogą się powtarzać.", ErrorCodes.AppointmentDuplicateService);
    }

    var conflictingGroup = services
      .Where(s => !string.IsNullOrWhiteSpace(s.ComboGroup))
      .GroupBy(s => s.ComboGroup!.Trim().ToLowerInvariant())
      .Any(g => g.Count() > 1);

    if (conflictingGroup)
    {
      throw new AppointmentBookingRuleException(
        "Nie można połączyć dwóch usług tego samego typu (grupa wariantów).",
        ErrorCodes.AppointmentComboGroupConflict);
    }

    EnsureValidAddons(services);
  }

  /// <summary>
  /// Reguły dodatków: (1) dodatek nie może być rezerwowany bez usługi głównej; (2) każdy wybrany
  /// dodatek musi być dozwolony przez którąś z obecnych w combo usług głównych (lista
  /// <see cref="Service.Addons"/>). Usługa główna = <see cref="Service.IsAddon"/> = false.
  /// </summary>
  private static void EnsureValidAddons(IReadOnlyList<Service> services)
  {
    var addons = services.Where(s => s.IsAddon).ToList();
    if (addons.Count == 0)
    {
      return;
    }

    var mains = services.Where(s => !s.IsAddon).ToList();
    if (mains.Count == 0)
    {
      throw new AppointmentBookingRuleException(
        "Usługa dodatkowa wymaga wybrania usługi głównej.",
        ErrorCodes.AppointmentAddonRequiresMain);
    }

    var allowedAddonIds = mains
      .SelectMany(m => m.Addons.Select(a => a.AddonServiceId))
      .ToHashSet();

    if (addons.Any(a => !allowedAddonIds.Contains(a.Id)))
    {
      throw new AppointmentBookingRuleException(
        "Wybrany dodatek nie pasuje do wybranej usługi głównej.",
        ErrorCodes.AppointmentAddonNotAllowed);
    }
  }
}

/// <summary>
/// Buduje pozycje combo (<see cref="AppointmentServiceLine"/>) rozwiązując czas i cenę każdej usługi
/// wobec pracownika (per-pracownik override z EmployeeService). Kolejność = kolejność wejściowa
/// (pierwsza usługa = „główna"). Rzuca <see cref="EmployeeServiceMissingException"/>, gdy pracownik
/// nie świadczy którejś z usług.
/// </summary>
public static class AppointmentComboBuilder
{
  public static List<AppointmentServiceLine> BuildLines(Employee employee, IReadOnlyList<Service> orderedServices)
  {
    var lines = new List<AppointmentServiceLine>(orderedServices.Count);
    foreach (var service in orderedServices)
    {
      var price = employee.CalculateTotalPrice(service.Id, service.Price); // rzuca, gdy pracownik nie ma usługi
      var duration = employee.ResolveServiceDurationMinutes(service.Id, service.DurationInMinutes);
      lines.Add(new AppointmentServiceLine(service.Id, duration, price));
    }

    return lines;
  }

  public static int TotalDurationMinutes(IReadOnlyList<AppointmentServiceLine> lines) => lines.Sum(l => l.DurationMinutes);
}
