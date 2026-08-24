namespace App.Application.VatRates.Dtos;

public record VatRateDto(Guid Id, string Name, decimal Value, bool IsDefault, bool IsActive);