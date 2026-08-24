namespace App.Domain.Aggregates.SmsTemplateAggregate;

/// <summary>
/// Stan workflow akceptacji custom szablonu SMS przez administratora platformy.
/// </summary>
public enum SmsTemplateStatus
{
  /// <summary>Brak treści oczekującej i brak zatwierdzonej — rekord świeży/wyczyszczony.</summary>
  None = 0,

  /// <summary>Salon zapisał treść — czeka na ręczną akceptację admina (SystemAdmin).</summary>
  PendingApproval = 1,

  /// <summary>Admin zatwierdził treść — <see cref="SmsTemplate.Body"/> jest aktywne przy wysyłce.</summary>
  Approved = 2,

  /// <summary>Admin odrzucił treść (z powodem). Poprzednia zatwierdzona treść — jeśli była — pozostaje aktywna.</summary>
  Rejected = 3,
}
