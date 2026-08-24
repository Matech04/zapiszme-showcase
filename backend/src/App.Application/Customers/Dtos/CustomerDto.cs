namespace App.Application.Customers.Dtos;

public record CustomerDto(
  Guid Id,
  string FirstName,
  string LastName,
  string Email,
  string? PhoneNumber,
  DateOnly CreatedAt,
  string GeneralNotes,
  int Source,
  bool IsWhitelisted,
  string? InstagramNick);