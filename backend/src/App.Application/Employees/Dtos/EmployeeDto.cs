using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.Employees.Dtos;

public record EmployeeDto(
  Guid Id,
  Guid? UserId,
  string FirstName,
  string LastName,
  string Email,
  string? Specialization,
  SlotGenerationMode SlotGenerationMode,
  // Rola konta logowania (Owner/Manager/Employee/Kiosk) — z Identity, nie z aggregate.
  // null = pracownik bez powiązanego konta (czysty zasób kalendarza).
  string? Role,
  // Zaproszenie wysłane, ale konto jeszcze nieaktywowane (Identity EmailConfirmed == false).
  bool InvitePending);
