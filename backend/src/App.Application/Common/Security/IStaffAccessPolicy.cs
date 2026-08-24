namespace App.Application.Common.Security;

/// <summary>
/// JEDNO miejsce, które wie, kto może oglądać i zmieniać kalendarz w salonie. Łączy dwie osie:
/// rolę (Owner/Manager/Admin/Kiosk/Employee) oraz <c>StaffCalendarVisibilityPolicy</c> tenanta.
///
/// Powstało, bo ta sama reguła była przepisana w prywatnych helperach trzech kontrolerów
/// (Appointments, Deposits, Employees) plus w <c>EmployeeMutationAccess</c> — i już się rozjechała:
/// kontroler wizyt znał politykę widoczności, a kontroler pracowników nie, więc pracownik widział
/// cudze wizyty, ale nie cudze godziny pracy („Dzień wolny" u kolegi).
///
/// Metody <c>Ensure*</c> rzucają <see cref="App.Domain.Exceptions.ForbiddenAccessException"/>,
/// mapowany na 403 przez <c>GlobalFallbackExceptionHandler</c>. Serwis jest SCOPED i pamięta
/// politykę tenanta na czas jednego żądania — patrz <c>StaffAccessPolicy</c>.
/// </summary>
public interface IStaffAccessPolicy
{
  /// <summary>
  /// Odczyt kalendarza (wizyty, godziny pracy, urlopy) danego pracownika.
  /// Przechodzi: własny kalendarz, Owner/Manager/Admin, Kiosk, oraz pracownik w salonie
  /// z polityką ≥ <c>TeamReadOnly</c>.
  /// </summary>
  Task EnsureCanViewEmployeeCalendarAsync(Guid employeeId, CancellationToken ct);

  /// <summary>
  /// Zapis w kalendarzu danego pracownika (utworzenie/przełożenie wizyty na jego grafiku).
  /// Węższe niż odczyt: <c>TeamReadOnly</c> daje wgląd, ale NIE prawo zapisu — potrzebny
  /// <c>TeamFull</c>.
  /// </summary>
  Task EnsureCanMutateEmployeeCalendarAsync(Guid employeeId, CancellationToken ct);

  /// <summary>
  /// Zapis na ISTNIEJĄCEJ wizycie (status, notatka, cena, usługi, zamiana). Rozwiązuje pracownika
  /// z wizyty i stosuje <see cref="EnsureCanMutateEmployeeCalendarAsync"/>.
  /// Nie istniejąca wizyta = <c>NotFoundException</c>, nie 403 — brak uprawnień i brak zasobu to
  /// różne odpowiedzi.
  /// </summary>
  Task EnsureCanMutateAppointmentAsync(Guid appointmentId, CancellationToken ct);

  /// <summary>
  /// Zakres, do którego wolno zawęzić ODCZYT listy wizyt. Zwraca <c>employeeId</c>, po którym
  /// należy filtrować, albo <c>null</c> = cały salon.
  ///
  /// Dzięki temu <c>OwnCalendarOnly</c> nie pobiera cudzych wizyt tylko po to, żeby je odrzucić.
  /// Jawne żądanie cudzego pracownika przy <c>OwnCalendarOnly</c> to 403 (a nie ciche zawężenie),
  /// bo taki request jest błędem klienta, nie zapytaniem ogólnym.
  /// </summary>
  Task<Guid?> ResolveCalendarReadScopeAsync(Guid? requestedEmployeeId, CancellationToken ct);

  /// <summary>
  /// ODCZYT danych potrzebnych do wyświetlenia cudzego kalendarza: godziny pracy, dni specjalne,
  /// urlopy, przypisane usługi. Szerszy niż <see cref="EnsureSelfOrStaffManager"/> (pracownik z
  /// polityką ≥ TeamReadOnly widzi kolegów), ale ograniczony do TEGO salonu — pracownik obcego
  /// tenanta to 403, a nie 404 z warstwy aplikacji.
  /// </summary>
  Task EnsureCanReadEmployeeCalendarDataAsync(Guid employeeId, CancellationToken ct);

  /// <summary>
  /// ZAPIS i wrażliwe odczyty PROFILU pracownika. Employee tylko na sobie; Owner/Manager/Admin na
  /// dowolnym. Nie zna polityki kalendarza — celowo, bo `TeamFull` daje prawo do cudzego
  /// KALENDARZA, nie do cudzego PROFILU.
  /// </summary>
  void EnsureSelfOrStaffManager(Guid targetEmployeeId);
}
