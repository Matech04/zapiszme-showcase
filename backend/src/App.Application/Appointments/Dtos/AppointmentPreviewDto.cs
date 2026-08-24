using App.Domain.Aggregates.AppointmentAggregate;

namespace App.Application.Appointments.Dtos;

public record AppointmentPreviewDto(
  Guid Id,
  Guid EmployeeId,
  string EmployeeFirstName,
  string EmployeeLastName,
  string CustomerFirstName,
  string CustomerLastName,
  string CustomerPhoneNumber,
  string CustomerEmail,
  string ServiceName,
  DateOnly Date,
  TimeOnly StartTime,
  TimeOnly EndTime,
  AppointmentStatus Status,
  bool IsGuest
  );
