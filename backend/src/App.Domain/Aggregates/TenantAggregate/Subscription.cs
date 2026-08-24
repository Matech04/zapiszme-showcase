namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Subskrypcja salonu — per-seat. Cena i SMS allowance obliczane on-the-fly z
/// <see cref="Seats"/> + <see cref="IsFoundingMember"/>, nigdy nie cachowane.
/// Founding Member to flaga dożywotnia dla pierwszych klientek (cena bazowa 49 zł vs 79 zł).
/// </summary>
public class Subscription
{
  /// <summary>Cena bazowa standard (PLN grosze) za pierwsze stanowisko miesięcznie — 79 zł.</summary>
  public const int BasePriceGrosze = 7900;

  /// <summary>Cena bazowa Founding Member (PLN grosze) za pierwsze stanowisko miesięcznie — 49 zł.</summary>
  public const int FoundingMemberBasePriceGrosze = 4900;

  /// <summary>Dopłata za każde kolejne stanowisko (PLN grosze) — 35 zł.</summary>
  public const int AdditionalSeatPriceGrosze = 3500;

  /// <summary>SMS w cenie planu dla 1 stanowiska miesięcznie.</summary>
  public const int BaseSmsAllowance = 200;

  /// <summary>Dopłata SMS allowance za każde kolejne stanowisko.</summary>
  public const int AdditionalSeatSmsAllowance = 150;

  /// <summary>Cena nadwyżki SMS (PLN grosze) — 0.15 zł. Tylko do liczenia, billing dopiero z integracją płatności.</summary>
  public const int OverageSmsPriceGrosze = 15;

  /// <summary>Domyślna długość trialu w dniach dla nowego tenant'a.</summary>
  public const int DefaultTrialDays = 30;

  public SubscriptionStatus Status { get; private set; }
  public int Seats { get; private set; } = 1;
  public bool IsFoundingMember { get; private set; }

  /// <summary>Koniec trialu — wypełnione tylko gdy <see cref="Status"/> == Trial.</summary>
  public DateTimeOffset? TrialEndsAt { get; private set; }

  /// <summary>Koniec bieżącego okresu rozliczeniowego — wypełnione tylko gdy Status == Active/PastDue.</summary>
  public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }

  /// <summary>
  /// FK do aktywnego <c>PromoCodeRedemption</c>, jeżeli salon ma aplikowany rabat. Pricing service
  /// ładuje powiązany redemption + jego <c>DiscountSnapshot</c> żeby obliczyć finalną cenę.
  /// </summary>
  public Guid? ActivePromoCodeRedemptionId { get; private set; }

  /// <summary>
  /// Twardy miesięczny limit wysłanych SMS (anti-abuse kill-switch). <c>null</c> = brak override,
  /// stosujemy <see cref="MonthlySmsAllowance"/> jako limit. Edytowalny przez admina per salon.
  /// Po osiągnięciu <see cref="EffectiveMonthlySmsCap"/> wysyłka SMS (powiadomienia + OTP) jest blokowana.
  /// </summary>
  public int? MonthlySmsHardCap { get; private set; }

  private Subscription() { }

  /// <summary>Startowa subskrypcja nowego tenant'a — Trial 30 dni, 1 stanowisko, bez Founding Member.</summary>
  public static Subscription StartTrial(int days = DefaultTrialDays) => new()
  {
    Status = SubscriptionStatus.Trial,
    Seats = 1,
    IsFoundingMember = false,
    TrialEndsAt = DateTimeOffset.UtcNow.AddDays(days),
    CurrentPeriodEndsAt = null,
  };

  /// <summary>
  /// Admin override — wymuszenie konkretnego stanu subskrypcji niezależnie od ścieżki biznesowej.
  /// Używany WYŁĄCZNIE przez <c>SetTenantSubscriptionCommand</c> (endpoint admin).
  /// </summary>
  public static Subscription AdminReset(
    SubscriptionStatus status,
    int seats,
    bool isFoundingMember,
    DateTimeOffset? trialEndsAt,
    DateTimeOffset? currentPeriodEndsAt,
    int? monthlySmsHardCap = null) => new()
  {
    Status = status,
    Seats = Math.Max(1, seats),
    IsFoundingMember = isFoundingMember,
    TrialEndsAt = trialEndsAt,
    CurrentPeriodEndsAt = currentPeriodEndsAt,
    MonthlySmsHardCap = monthlySmsHardCap is < 0 ? null : monthlySmsHardCap,
  };

  /// <summary>Aktywuj subskrypcję po opłaceniu — ustaw seats, founding member, okres rozliczeniowy +1 mies.</summary>
  public void Activate(int seats, bool foundingMember)
  {
    if (seats < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(seats), "Liczba stanowisk musi być >= 1.");
    }
    Status = SubscriptionStatus.Active;
    Seats = seats;
    IsFoundingMember = foundingMember;
    TrialEndsAt = null;
    CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1);
  }

  /// <summary>
  /// Zmień liczbę stanowisk. Soft limit — każda wartość ≥ 1 OK. Nowa cena obowiązuje od następnego cyklu
  /// (cache'owana nigdzie nie jest, więc <see cref="MonthlyPriceInGrosze"/> natychmiast pokazuje nową wartość).
  /// </summary>
  public void ChangeSeats(int newSeats)
  {
    if (newSeats < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(newSeats), "Liczba stanowisk musi być >= 1.");
    }
    Seats = newSeats;
  }

  /// <summary>Płatność nie potwierdzona / okres się zakończył — ogranicz dostęp.</summary>
  public void MarkPastDue()
  {
    Status = SubscriptionStatus.PastDue;
    CurrentPeriodEndsAt = null;
  }

  /// <summary>Klient zrezygnował. Seats/FoundingMember zachowane na wypadek reactivate.</summary>
  public void Cancel()
  {
    Status = SubscriptionStatus.Canceled;
    CurrentPeriodEndsAt = null;
  }

  /// <summary>Flaga dożywotnia — pierwsze ~10 klientek (cena bazowa 49 zł zamiast 79 zł). Admin-only.</summary>
  public void MarkAsFoundingMember()
  {
    IsFoundingMember = true;
  }

  /// <summary>
  /// Podłącz redemption do subskrypcji. Tylko jeden aktywny rabat per tenant w danym czasie
  /// (decyzja produktowa — brak stackingu). Wywołujący odpowiada za to, żeby poprzedni redemption
  /// był deaktywowany przed wywołaniem (lub redemption automatycznie wygasł).
  /// </summary>
  public void ApplyPromoRedemption(Guid redemptionId)
  {
    if (redemptionId == Guid.Empty)
    {
      throw new ArgumentException("RedemptionId required.", nameof(redemptionId));
    }
    ActivePromoCodeRedemptionId = redemptionId;
  }

  /// <summary>Odepnij rabat (np. wygasł, klient zrezygnował). Redemption nadal istnieje dla historii.</summary>
  public void ClearPromoRedemption()
  {
    ActivePromoCodeRedemptionId = null;
  }

  /// <summary>Wydłuż okres rozliczeniowy o N miesięcy (efekt <c>FreeMonths</c>). Jeśli okres nie ustawiony — wystartuj od now.</summary>
  public void ExtendCurrentPeriod(int months)
  {
    if (months <= 0) throw new ArgumentOutOfRangeException(nameof(months));
    var baseline = CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow;
    CurrentPeriodEndsAt = baseline.AddMonths(months);
  }

  /// <summary>Wydłuż okres trialu o N dni (efekt <c>TrialExtension</c>). Działa tylko w statusie Trial.</summary>
  public void ExtendTrial(int days)
  {
    if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days));
    if (Status != SubscriptionStatus.Trial)
    {
      throw new InvalidOperationException("Trial extension can be applied only to trial subscriptions.");
    }
    var baseline = TrialEndsAt ?? DateTimeOffset.UtcNow;
    TrialEndsAt = baseline.AddDays(days);
  }

  /// <summary>Cena miesięczna (grosze): (49 lub 79) + 35 × (Seats - 1).</summary>
  public int MonthlyPriceInGrosze =>
    (IsFoundingMember ? FoundingMemberBasePriceGrosze : BasePriceGrosze)
    + AdditionalSeatPriceGrosze * (Seats - 1);

  /// <summary>SMS w cenie miesięcznie: 200 + 150 × (Seats - 1).</summary>
  public int MonthlySmsAllowance =>
    BaseSmsAllowance + AdditionalSeatSmsAllowance * (Seats - 1);

  /// <summary>
  /// Efektywny twardy limit SMS na miesiąc — override admina (<see cref="MonthlySmsHardCap"/>)
  /// albo, gdy nie ustawiony, limit z planu (<see cref="MonthlySmsAllowance"/>). Po jego osiągnięciu
  /// wysyłka SMS jest blokowana (anti-abuse).
  /// </summary>
  public int EffectiveMonthlySmsCap => MonthlySmsHardCap ?? MonthlySmsAllowance;

  /// <summary>Admin ustawia/zdejmuje twardy limit SMS. <c>null</c> = wróć do limitu z planu. Ujemne niedozwolone.</summary>
  public void SetMonthlySmsHardCap(int? cap)
  {
    if (cap is < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(cap), "Limit SMS nie może być ujemny.");
    }
    MonthlySmsHardCap = cap;
  }

  /// <summary>
  /// Efektywny status — Trial po wygaśnięciu jest traktowany jak PastDue (frontend i autoryzacja
  /// dostępu używają tego pola). Po stronie bazy status pozostaje Trial dopóki background job nie
  /// przeniesie go do PastDue.
  /// </summary>
  public SubscriptionStatus EffectiveStatus =>
    Status == SubscriptionStatus.Trial && TrialEndsAt is { } end && DateTimeOffset.UtcNow > end
      ? SubscriptionStatus.PastDue
      : Status;

  public bool IsTrialActive =>
    Status == SubscriptionStatus.Trial && TrialEndsAt is { } end && DateTimeOffset.UtcNow <= end;

  public int DaysRemainingInTrial =>
    IsTrialActive ? Math.Max(0, (int)Math.Ceiling((TrialEndsAt!.Value - DateTimeOffset.UtcNow).TotalDays)) : 0;
}
