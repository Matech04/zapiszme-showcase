using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Tenants.Dtos;

public record DepositSettingsDto(
  bool Enabled,
  DepositMode Mode,
  decimal Value,
  DepositInstrument Instrument);
