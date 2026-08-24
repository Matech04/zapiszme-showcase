using App.Domain.Common;

namespace App.Application.Employees.Dtos;

public record EmployeeServiceDto(Guid ServiceId, int? CustomDuration, Money? CustomPrice);