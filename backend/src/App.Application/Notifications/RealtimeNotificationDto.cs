namespace App.Application.Notifications;

/// <summary>
/// Ładunek powiadomienia in-app pushowanego do klienta dashboardu (SignalR). Żyje w App.Application,
/// żeby kanał (App.Infrastructure) i implementacja transportu (App.Api) mogły się do niego odwołać.
/// <c>Type</c> jako <see cref="int"/> — stabilny kontrakt dla wygenerowanego klienta TS.
/// </summary>
public record RealtimeNotificationDto(
  Guid Id,
  Guid TenantId,
  int Type,
  string Subject,
  string ShortText,
  Guid? ReferenceId,
  DateTime CreatedAtUtc,
  bool Read);
