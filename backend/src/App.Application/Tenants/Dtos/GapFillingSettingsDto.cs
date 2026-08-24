using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Tenants.Dtos;

public record GapFillingSettingsDto(
  GapFillingMode Mode,
  int BufferMinutes,
  int LookaheadSlots);
