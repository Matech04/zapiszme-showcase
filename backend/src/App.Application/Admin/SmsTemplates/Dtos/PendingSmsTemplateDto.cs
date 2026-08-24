using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Admin.SmsTemplates.Dtos;

/// <summary>
/// Pozycja adminowej kolejki moderacji custom szablonów SMS: który salon, jaki typ, oczekująca treść
/// + podgląd po interpolacji przykładowymi danymi (jak realnie pójdzie SMS).
/// </summary>
public record PendingSmsTemplateDto(
  Guid TemplateId,
  Guid TenantId,
  string TenantName,
  NotificationType NotificationType,
  string TypeLabel,
  string PendingBody,
  string PendingPreview,
  DateTime? SubmittedAtUtc);
