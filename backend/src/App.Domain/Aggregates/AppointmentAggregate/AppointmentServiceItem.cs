using App.Domain.Common;

namespace App.Domain.Aggregates.AppointmentAggregate;

/// <summary>
/// Pozycja usługi w wizycie (combo). Snapshotuje czas i cenę wyznaczone przy rezerwacji
/// (z uwzględnieniem per-pracownik override z EmployeeService), żeby późniejsza zmiana cennika
/// usługi nie zmieniała wstecz wartości wizyty. Nazwa usługi dołączana jest na żywo przez join
/// (FK <see cref="ServiceId"/>), spójnie z dotychczasowymi projekcjami DTO.
/// Cena trzymana jest jako pola skalarne (a nie owned <see cref="Money"/>), bo owned-w-owned-collection
/// nie jest wspierane przez provider InMemory używany w testach jednostkowych.
/// </summary>
public class AppointmentServiceItem : Entity, ITenantEntity
{
  public Guid TenantId { get; private set; }
  public Guid AppointmentId { get; private set; }
  public Guid ServiceId { get; private set; }

  /// <summary>Snapshot czasu trwania tej usługi (minuty) — suma po pozycjach daje czas wizyty.</summary>
  public int DurationMinutes { get; private set; }

  /// <summary>Snapshot ceny (kwota) — suma po pozycjach daje <c>Appointment.TotalPrice</c>.</summary>
  public decimal PriceAmount { get; private set; }

  /// <summary>Waluta snapshotu ceny (ISO 4217).</summary>
  public string PriceCurrency { get; private set; } = "PLN";

  /// <summary>Kolejność w combo (0 = primary). Decyduje o sekwencji i o tym, która usługa jest „główna".</summary>
  public int Position { get; private set; }

  /// <summary>Wygodny odczyt ceny jako <see cref="Money"/> (nie mapowane do kolumny).</summary>
  public Money Price => new(PriceAmount, PriceCurrency);

  public AppointmentServiceItem(Guid tenantId, Guid appointmentId, Guid serviceId, int durationMinutes, Money price, int position)
  {
    Id = Guid.NewGuid();
    TenantId = tenantId;
    AppointmentId = appointmentId;
    ServiceId = serviceId;
    DurationMinutes = durationMinutes;
    PriceAmount = price.Amount;
    PriceCurrency = price.Currency;
    Position = position;
  }

  private AppointmentServiceItem() { }
}

/// <summary>
/// Wejściowa pozycja combo używana przy tworzeniu/aktualizacji wizyty — (usługa, czas, cena) już
/// rozwiązane wobec pracownika. Aggregate buduje z niej <see cref="AppointmentServiceItem"/>.
/// </summary>
public readonly record struct AppointmentServiceLine(Guid ServiceId, int DurationMinutes, Money Price);
