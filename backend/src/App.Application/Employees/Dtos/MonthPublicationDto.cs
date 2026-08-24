namespace App.Application.Employees.Dtos;

/// <summary>
/// Decyzja o otwarciu zapisów na dany miesiąc dla pracownika. Brak wpisu dla miesiąca
/// oznacza „otwarty w granicach horyzontu rezerwacji salonu" — nie „zamknięty".
/// </summary>
/// <param name="Year">Rok miesiąca.</param>
/// <param name="Month">Miesiąc 1-12.</param>
/// <param name="OpensOn">
/// Dzień, w którym miesiąc otworzy się sam. <c>null</c> = zamknięty bezterminowo, do ręcznego otwarcia.
/// </param>
public record MonthPublicationDto(
  int Year,
  int Month,
  DateOnly? OpensOn
  );
