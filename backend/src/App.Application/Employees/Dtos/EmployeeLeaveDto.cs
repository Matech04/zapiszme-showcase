using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Application.Employees.Dtos;

/// <summary>
/// Nieobecność pracownika.
///
/// <see cref="AbsenceType"/> jest NULLOWALNY: kolega z zespołu i konto „Recepcja" dostają zakres
/// dat bez powodu nieobecności. <c>AbsenceType.SickLeave</c> to dana o zdrowiu (art. 9 RODO), a do
/// narysowania kalendarza wystarczy „niedostępny w dniach X–Y". Pełną wartość widzi sam pracownik
/// oraz Owner/Manager/Admin.
///
/// <see cref="AbsenceStatus"/> NIE jest maskowany — „zatwierdzone / oczekujące / odrzucone" to stan
/// obiegu wniosku, nie informacja o zdrowiu, a kalendarz musi odróżnić urlop zatwierdzony (zdejmuje
/// pas pracy) od oczekującego (jeszcze nie).
///
/// <see cref="BlocksDay"/> istnieje właśnie dlatego, że typ bywa zamaskowany. Front pytał dotąd
/// „czy typ to Vacation albo SickLeave?", żeby zablokować umawianie wizyt — po zamaskowaniu typu
/// to pytanie nie ma odpowiedzi, a odpowiedź „nie" pozwoliłaby umówić klientkę na czyjeś L4.
/// Serwer liczy je sam i oddaje SAM FAKT niedostępności, bez ujawniania przyczyny.
/// </summary>
public record EmployeeLeaveDto(
  Guid Id,
  DateOnly StartDate,
  DateOnly EndDate,
  AbsenceType? AbsenceType,
  AbsenceStatus AbsenceStatus,
  bool BlocksDay);
