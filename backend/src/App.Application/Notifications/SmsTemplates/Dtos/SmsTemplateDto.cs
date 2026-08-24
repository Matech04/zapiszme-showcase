using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Notifications.SmsTemplates.Dtos;

/// <summary>
/// Widok jednego personalizowalnego typu SMS dla salonu: domyślna (hardcoded) treść jako podgląd,
/// aktualnie zatwierdzona treść, treść oczekująca na akceptację, status workflow + dostępne placeholdery.
/// </summary>
public record SmsTemplateDto(
  NotificationType NotificationType,
  string TypeLabel,
  string DefaultPreview,
  string? ApprovedBody,
  string? PendingBody,
  SmsTemplateStatus Status,
  string? RejectionReason,
  DateTime? SubmittedAtUtc,
  DateTime? ReviewedAtUtc,
  IReadOnlyList<string> AllowedPlaceholders);
