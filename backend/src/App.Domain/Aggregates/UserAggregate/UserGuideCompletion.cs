using App.Domain.Common;

namespace App.Domain.Aggregates.UserAggregate;

/// <summary>
/// Znacznik „ten użytkownik przeszedł już ten przewodnik". Jeden wiersz = jedna para
/// (użytkownik, przewodnik); unikalny indeks czyni zapis idempotentnym.
///
/// Stan celowo NIE jest <c>ITenantEntity</c>. Przewodniki są własnością OSOBY, nie salonu:
/// ta sama osoba na telefonie i laptopie ma widzieć jeden postęp, a właściciel i pracownik
/// tego samego salonu — osobny. Encja musi też działać dla konta bez tenanta (admin
/// systemowy) oraz dla właściciela przed utworzeniem salonu, więc filtr tenanta nie miałby
/// czego zaczepić. Izolację daje filtr po <see cref="UserId"/> z claima — dokładnie tak, jak
/// robi to <c>GetOnboardingStateQuery</c>, który z tego samego powodu omija <c>TenantHandler</c>.
///
/// <see cref="GuideId"/> to stabilny identyfikator z rejestru przewodników w dashboardzie
/// (<c>core/guides/guides.registry.ts</c>). Backend go NIE waliduje względem listy — celowo,
/// żeby dodanie przewodnika na froncie nie wymagało deployu API. Chroni go za to walidator
/// formatu i limit wierszy na użytkownika.
/// </summary>
public class UserGuideCompletion : Entity, IAggregateRoot
{
  /// <summary>Identity user, którego dotyczy postęp.</summary>
  public Guid UserId { get; private set; }

  /// <summary>Identyfikator przewodnika z rejestru front-endu (np. <c>set-weekly-schedule</c>).</summary>
  public string GuideId { get; private set; } = string.Empty;

  /// <summary>Moment pierwszego ukończenia. Ponowne odtworzenie przewodnika go NIE nadpisuje.</summary>
  public DateTime CompletedAtUtc { get; private set; }

  private UserGuideCompletion() { }

  public static UserGuideCompletion Create(Guid userId, string guideId, DateTime nowUtc)
  {
    Guard.AgainstEmptyString(guideId, nameof(guideId));

    return new UserGuideCompletion
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      GuideId = guideId,
      CompletedAtUtc = nowUtc,
    };
  }
}
