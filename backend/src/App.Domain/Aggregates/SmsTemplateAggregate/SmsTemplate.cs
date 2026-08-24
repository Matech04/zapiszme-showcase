using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;

namespace App.Domain.Aggregates.SmsTemplateAggregate;

/// <summary>
/// Spersonalizowany szablon treści SMS salonu dla konkretnego <see cref="NotificationType"/> (tylko
/// powiadomienia DO KLIENTA — NIE OTP, NIE *ToSalon). Jeden rekord na (TenantId, NotificationType).
///
/// Workflow akceptacji przez administratora platformy (SystemAdmin):
/// 1. Salon edytuje treść → trafia do <see cref="PendingBody"/> + <see cref="Status"/>=PendingApproval.
/// 2. Admin akceptuje → <see cref="Body"/> = PendingBody, Status=Approved, PendingBody wyczyszczone.
/// 3. Admin odrzuca → Status=Rejected (+ <see cref="RejectionReason"/>); <see cref="Body"/> bez zmian
///    (poprzednia zatwierdzona treść, jeśli była, pozostaje aktywna).
///
/// Wysyłka używa <see cref="Body"/> WYŁĄCZNIE gdy Status=Approved i Body niepuste; inaczej fallback
/// do hardcoded <c>SmsTextBuilder</c> — zero regresji dla salonów bez własnych szablonów.
/// </summary>
public class SmsTemplate : Entity, ITenantEntity, IAggregateRoot
{
  public Guid TenantId { get; private set; }

  public NotificationType NotificationType { get; private set; }

  /// <summary>Zatwierdzona, aktywna treść. <c>null</c> dopóki admin nic nie zatwierdził.</summary>
  public string? Body { get; private set; }

  /// <summary>Treść zapisana przez salon, oczekująca na akceptację. <c>null</c> gdy nic nie czeka.</summary>
  public string? PendingBody { get; private set; }

  public SmsTemplateStatus Status { get; private set; } = SmsTemplateStatus.None;

  /// <summary>UTC zgłoszenia treści do akceptacji (ostatnia edycja przez salon).</summary>
  public DateTime? SubmittedAtUtc { get; private set; }

  /// <summary>UTC ostatniej decyzji admina (approve/reject).</summary>
  public DateTime? ReviewedAtUtc { get; private set; }

  /// <summary>Powód odrzucenia podany przez admina. <c>null</c> gdy nieodrzucone.</summary>
  public string? RejectionReason { get; private set; }

  private SmsTemplate() { }

  public static SmsTemplate Create(Guid tenantId, NotificationType notificationType) => new()
  {
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    NotificationType = notificationType,
    Status = SmsTemplateStatus.None,
  };

  /// <summary>
  /// Salon zgłasza nową treść do akceptacji. Czyści ewentualny poprzedni powód odrzucenia.
  /// Zatwierdzona <see cref="Body"/> (jeśli była) pozostaje aktywna do czasu akceptacji nowej.
  /// </summary>
  public void SubmitForApproval(string body, DateTime nowUtc)
  {
    var trimmed = (body ?? string.Empty).Trim();
    if (trimmed.Length == 0)
    {
      throw new ArgumentException("Treść szablonu SMS nie może być pusta.", nameof(body));
    }

    PendingBody = trimmed;
    Status = SmsTemplateStatus.PendingApproval;
    SubmittedAtUtc = nowUtc;
    RejectionReason = null;
    ReviewedAtUtc = null;
  }

  /// <summary>Admin zatwierdza oczekującą treść — staje się aktywna.</summary>
  public void Approve(DateTime nowUtc)
  {
    if (Status != SmsTemplateStatus.PendingApproval || string.IsNullOrWhiteSpace(PendingBody))
    {
      throw new InvalidOperationException("Brak treści oczekującej na akceptację.");
    }

    Body = PendingBody;
    PendingBody = null;
    Status = SmsTemplateStatus.Approved;
    RejectionReason = null;
    ReviewedAtUtc = nowUtc;
  }

  /// <summary>Admin odrzuca oczekującą treść (z powodem). Zatwierdzona <see cref="Body"/> bez zmian.</summary>
  public void Reject(string? reason, DateTime nowUtc)
  {
    if (Status != SmsTemplateStatus.PendingApproval || string.IsNullOrWhiteSpace(PendingBody))
    {
      throw new InvalidOperationException("Brak treści oczekującej na odrzucenie.");
    }

    PendingBody = null;
    Status = SmsTemplateStatus.Rejected;
    RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    ReviewedAtUtc = nowUtc;
  }

  /// <summary>
  /// Czy ten szablon ma aktywną, zatwierdzoną treść do użycia przy wysyłce. <see cref="Body"/> jest
  /// ustawiane WYŁĄCZNIE przez <see cref="Approve"/> i nie jest czyszczone przez ponowne zgłoszenie
  /// ani odrzucenie — dzięki temu poprzednia zatwierdzona treść pozostaje aktywna, gdy nowa edycja
  /// czeka na akceptację lub została odrzucona. Status NIE jest tu warunkiem.
  /// </summary>
  public bool HasApprovedBody => !string.IsNullOrWhiteSpace(Body);
}
