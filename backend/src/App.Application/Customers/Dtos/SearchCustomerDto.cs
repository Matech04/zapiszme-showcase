public record SearchCustomerDto(
  string FirstName,
  string LastName,
  string? PhoneNumber,
  Guid Id
);