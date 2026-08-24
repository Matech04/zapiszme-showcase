namespace App.Domain.Aggregates.CustomerAggregate;

/// <summary>
/// Pochodzenie rekordu klienta. Pozwala odróżnić klientów dodanych ręcznie od tych
/// utworzonych automatycznie po publicznej rezerwacji (np. żeby ich profil nie miał
/// imienia/nazwiska — tylko kontakt).
/// </summary>
public enum CustomerSource
{
  Manual = 0,
  PublicBooking = 1,
  Imported = 2,
}
