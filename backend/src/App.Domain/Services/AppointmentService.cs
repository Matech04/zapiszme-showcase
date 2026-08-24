using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;

namespace App.Domain.Services;

public class AppointmentService : IAppointmentService
{
  private readonly IAppointmentRepository _appointmentRepository;

  public AppointmentService(IAppointmentRepository repository)
  {
    _appointmentRepository = repository;
  }

  public async Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
  {
    var appointmentCollision = await _appointmentRepository.HasCollisionAsync(employee.Id, timeRange, date, tenantId, ignoreAppointmentId);

    //Sprawdz czy nie ma kolizji z podanym dniem
    if (appointmentCollision)
    {
      return false;
    }

    // Zapis „poza grafikiem" (Panel): kolizja już sprawdzona — okno pracy/urlop pomijamy.
    return ignoreSchedule || employee.IsAvailable(timeRange, date);

  }

  public Task<bool> IsAvailableAsync(Employee employee, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
  {
    return IsAvailableAsync(employee, new TimeRange(startTime, endTime), date, tenantId, ignoreAppointmentId, ignoreSchedule);
  }

  public async Task<bool> IsAvailableAsync(Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds)
  {
    var appointmentCollision = await _appointmentRepository.HasCollisionAsync(employee.Id, timeRange, date, tenantId, ignoreAppointmentIds);

    if (appointmentCollision)
    {
      return false;
    }

    return employee.IsAvailable(timeRange, date);
  }

  // To ma zwracać liste slotów nie time range
  public List<TimeOnly> EmployeeAvailableSlotsList(
    List<TimeRange> schedule,
    List<TimeRange> appointments,
    Employee employee,
    int totalDurationMinutes,
    int appointmentSlotStepMinutes)
  {
    var AvailableTimeRangesList = TimeRangeHelper.CropTimeRange(schedule, appointments, appointmentSlotStepMinutes);

    var AvailableSlotsList = new List<TimeOnly>();

    foreach (var range in AvailableTimeRangesList)
    {
      var startTime = range.StartTime;
      var endTime = range.EndTime;
      var time = startTime;

      while (time <= endTime)
      {
        // Sprawdź czy całe combo (suma czasów usług) mieści się do końca okna pracy.
        var serviceEndTime = time.AddMinutes(totalDurationMinutes);

        if (serviceEndTime <= endTime)
        {
          AvailableSlotsList.Add(time);
        }
        else
        {
          break;
        }

        time = time.AddMinutes(appointmentSlotStepMinutes);
      }
    }

    return AvailableSlotsList;
  }

  // Tryb stałych slotów: brak okna pracy i siatki — slot jest oferowany dopóki [start, start+usługa]
  // nie nachodzi na istniejącą (niecancelowaną) wizytę. Operatorka świadomie ustawia te godziny;
  // nakładające się sloty to jej wybór — rezerwacja jednego konsumuje kolidujący.
  public List<TimeOnly> EmployeeFixedSlotsList(
    IReadOnlyList<TimeOnly> fixedStartTimes,
    List<TimeRange> appointments,
    Employee employee,
    int totalDurationMinutes)
  {
    var result = new List<TimeOnly>();

    foreach (var start in fixedStartTimes.Distinct().OrderBy(t => t))
    {
      var end = start.AddMinutes(totalDurationMinutes);

      // TimeOnly zawija się przez północ — usługa przekraczająca dobę nie mieści się w tym dniu.
      if (end <= start)
      {
        continue;
      }

      var candidate = new TimeRange(start, end);
      if (!appointments.Any(a => a.OverlapsWith(candidate)))
      {
        result.Add(start);
      }
    }

    return result;
  }
}
