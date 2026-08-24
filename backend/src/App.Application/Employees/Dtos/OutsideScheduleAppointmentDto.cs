namespace App.Application.Employees.Dtos;

/// <summary>
/// Wizyta która koliduje z nowo ustawianym grafikiem/urlopem. Zwracana w odpowiedzi
/// do UI, żeby pokazać użytkownikowi co dokładnie jest poza harmonogramem
/// (decyzja o anulowaniu/przeniesieniu należy do pracownika).
/// </summary>
public record OutsideScheduleAppointmentDto(
    Guid AppointmentId,
    Guid EmployeeId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime
);
