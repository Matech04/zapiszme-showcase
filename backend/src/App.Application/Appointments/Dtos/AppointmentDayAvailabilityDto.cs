namespace App.Application.Appointments.Dtos;

/// <summary>
/// Liczba wolnych slotów dla pojedynczego dnia — używane przez kalendarz w panelu
/// salonu do kolorowania kafelków dat (red/yellow/green) przy dodawaniu lub zmianie
/// terminu wizyty.
/// </summary>
public record AppointmentDayAvailabilityDto(
  DateOnly date,
  int availableCount
  );
