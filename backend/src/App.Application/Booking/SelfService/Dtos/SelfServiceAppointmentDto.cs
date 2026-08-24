namespace App.Application.Booking.SelfService.Dtos;

public record SelfServiceAppointmentDto(
  Guid Id,
  Guid EmployeeId,
  Guid ServiceId,
  // Pełny skład combo (wg Position; [0] == ServiceId). Potrzebny do liczenia dostępności przy
  // przekładaniu — inaczej wolne sloty/obłożenie liczyłyby się dla samej usługi głównej.
  IReadOnlyList<Guid> ServiceIds,
  string ServiceName,
  string EmployeeFirstName,
  string EmployeeLastName,
  DateOnly Date,
  TimeOnly StartTime,
  TimeOnly EndTime,
  string Status,
  bool CanCancel,
  bool CanReschedule);
