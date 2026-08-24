using App.Domain.Common;

namespace App.Domain.Aggregates.EmployeeAggregate;

/// <summary>
/// Decyzja salonu o tym, KIEDY dany miesiąc kalendarzowy staje się widoczny w publicznym bookingu.
/// Oś niezależna od tego, czy dostępność w tym miesiącu bierze się z grafiku powtarzalnego, czy
/// z samych dni specjalnych — dlatego publikacja jest własnością OKRESU, a nie encji grafiku.
///
/// Brak wiersza dla miesiąca = miesiąc otwarty, ograniczony wyłącznie horyzontem rezerwacji
/// (<see cref="Aggregates.TenantAggregate.Tenant.BookingHorizonDays"/>). Wiersz nadpisuje horyzont
/// w OBIE strony: potrafi otwarcie opóźnić („wrzesień pokaż 1 września") i przyspieszyć
/// („otwieramy grudzień już w październiku, bo święta").
/// </summary>
public class MonthPublication : Entity
{
  public int Year { get; private set; }
  public int Month { get; private set; }

  /// <summary>
  /// Dzień, od którego miesiąc jest widoczny dla klientów. <c>null</c> = zamknięty bezterminowo,
  /// do ręcznego otwarcia. Data w przeszłości = miesiąc już otwarty.
  /// </summary>
  public DateOnly? OpensOn { get; private set; }

  public MonthPublication(int year, int month, DateOnly? opensOn)
  {
    Guard.AgainstInvalidYearMonth(year, month);

    Id = Guid.NewGuid();
    Year = year;
    Month = month;
    OpensOn = opensOn;
  }

  private MonthPublication() { }

  internal void SetOpensOn(DateOnly? opensOn)
  {
    OpensOn = opensOn;
  }

  /// <summary>Czy miesiąc jest już otwarty dla klientów w dniu <paramref name="asOf"/>.</summary>
  public bool IsOpenAsOf(DateOnly asOf) => OpensOn.HasValue && OpensOn.Value <= asOf;

  /// <summary>Czy ten wiersz dotyczy miesiąca, w którym leży <paramref name="date"/>.</summary>
  public bool Covers(DateOnly date) => date.Year == Year && date.Month == Month;
}
