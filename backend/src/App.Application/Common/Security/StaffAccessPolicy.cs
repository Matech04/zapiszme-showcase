using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Common.Security;

/// <summary>
/// Implementacja <see cref="IStaffAccessPolicy"/>. Rejestrowana jako SCOPED.
///
/// Politykę tenanta czyta LENIWIE i pamięta na czas żądania — dziś ten sam `SELECT` z `Tenants`
/// leci do sześciu razy w jednym żądaniu (`swap` czyta ją czterokrotnie). Scope w ASP.NET to jedno
/// żądanie, a tenant rozwiązuje się raz, w `TenantIdentifierMiddleware`, zanim handler wystartuje —
/// więc instancja nigdy nie dożyje drugiego tenanta. Mimo to trzymamy w cache PARĘ
/// `(tenantId, policy)` i przeliczamy przy zmianie tenanta: jedno porównanie `Guid` zamienia
/// „to nie może się zdarzyć" na „a gdyby jednak, to zadziała poprawnie". Kod egzekwujący izolację
/// tenantów nie powinien opierać się na założeniu, którego nie sprawdza.
/// </summary>
internal sealed class StaffAccessPolicy : IStaffAccessPolicy
{
  private readonly IApplicationDbContext _context;
  private readonly ICurrentTenantService _currentTenant;
  private readonly ICurrentUserAccessor _currentUser;

  private (Guid TenantId, StaffCalendarVisibilityPolicy Policy)? _cachedPolicy;

  public StaffAccessPolicy(
      IApplicationDbContext context,
      ICurrentTenantService currentTenant,
      ICurrentUserAccessor currentUser)
  {
    _context = context;
    _currentTenant = currentTenant;
    _currentUser = currentUser;
  }

  /// <summary>
  /// Owner/Manager/Admin oraz Kiosk („Recepcja") obsługują wizyty całego zespołu i nie podlegają
  /// polityce widoczności. Kiosk nie ma własnego kalendarza, więc bez tego nie mógłby nic.
  /// </summary>
  private bool ManagesWholeSalon => _currentUser.CanManageOtherEmployees || _currentUser.IsDeskAccount;

  public async Task EnsureCanViewEmployeeCalendarAsync(Guid employeeId, CancellationToken ct)
  {
    if (ManagesWholeSalon)
    {
      return;
    }

    var callerEmployeeId = RequireCallerEmployeeId();
    if (callerEmployeeId == employeeId)
    {
      return;
    }

    var policy = await GetPolicyAsync(ct);
    if (policy is StaffCalendarVisibilityPolicy.TeamReadOnly or StaffCalendarVisibilityPolicy.TeamFull)
    {
      return;
    }

    throw new ForbiddenAccessException(
        "Widzisz wyłącznie własny kalendarz.",
        ErrorCodes.CalendarCannotViewOtherEmployee);
  }

  public async Task EnsureCanMutateEmployeeCalendarAsync(Guid employeeId, CancellationToken ct)
  {
    if (ManagesWholeSalon)
    {
      return;
    }

    var callerEmployeeId = RequireCallerEmployeeId();
    if (callerEmployeeId == employeeId)
    {
      return;
    }

    // TeamReadOnly celowo NIE wystarcza: daje wgląd w kalendarz zespołu, nie prawo zapisu.
    var policy = await GetPolicyAsync(ct);
    if (policy == StaffCalendarVisibilityPolicy.TeamFull)
    {
      return;
    }

    throw new ForbiddenAccessException(
        "Możesz zmieniać wyłącznie własny kalendarz.",
        ErrorCodes.CalendarCannotMutateOtherEmployee);
  }

  public async Task EnsureCanMutateAppointmentAsync(Guid appointmentId, CancellationToken ct)
  {
    if (ManagesWholeSalon)
    {
      return;
    }

    var employeeId = await _context.Appointments
      .AsNoTracking()
      .Where(a => a.Id == appointmentId)
      .Select(a => (Guid?)a.EmployeeId)
      .FirstOrDefaultAsync(ct);

    if (employeeId is null)
    {
      throw new NotFoundException(nameof(Appointment), appointmentId);
    }

    await EnsureCanMutateEmployeeCalendarAsync(employeeId.Value, ct);
  }

  public async Task<Guid?> ResolveCalendarReadScopeAsync(Guid? requestedEmployeeId, CancellationToken ct)
  {
    if (ManagesWholeSalon)
    {
      return requestedEmployeeId;
    }

    var callerEmployeeId = RequireCallerEmployeeId();

    var policy = await GetPolicyAsync(ct);
    if (policy != StaffCalendarVisibilityPolicy.OwnCalendarOnly)
    {
      return requestedEmployeeId;
    }

    if (requestedEmployeeId.HasValue && requestedEmployeeId.Value != callerEmployeeId)
    {
      throw new ForbiddenAccessException(
          "Widzisz wyłącznie własny kalendarz.",
          ErrorCodes.CalendarCannotViewOtherEmployee);
    }

    return callerEmployeeId;
  }

  public async Task EnsureCanReadEmployeeCalendarDataAsync(Guid employeeId, CancellationToken ct)
  {
    if (_currentUser.CanManageOtherEmployees || _currentUser.CallerEmployeeId == employeeId)
    {
      return;
    }

    // Rozszerzenie dotyczy WYŁĄCZNIE kolegów z tego samego salonu. Bez tego pracownik obcego
    // tenanta przeszedłby autoryzację i dostał 404 z zapytania zamiast 403.
    var tenantId = _currentTenant.TenantId ?? throw new NoTenantHeader();
    var isTeammate = await _context.Employees
      .AsNoTracking()
      .AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId, ct);

    if (!isTeammate)
    {
      throw new ForbiddenAccessException(
          "Ta operacja jest dozwolona tylko na własnym profilu albo przez rolę Owner/Manager.",
          ErrorCodes.EmployeeCannotMutateOtherProfile);
    }

    if (_currentUser.IsDeskAccount)
    {
      return;
    }

    var policy = await GetPolicyAsync(ct);
    if (policy is StaffCalendarVisibilityPolicy.TeamReadOnly or StaffCalendarVisibilityPolicy.TeamFull)
    {
      return;
    }

    throw new ForbiddenAccessException(
        "Ta operacja jest dozwolona tylko na własnym profilu albo przez rolę Owner/Manager.",
        ErrorCodes.EmployeeCannotMutateOtherProfile);
  }

  public void EnsureSelfOrStaffManager(Guid targetEmployeeId)
  {
    if (_currentUser.CanManageOtherEmployees)
    {
      return;
    }

    if (_currentUser.CallerEmployeeId is null)
    {
      throw new ForbiddenAccessException(
          "Brak powiązania konta z pracownikiem w tym salonie.",
          ErrorCodes.EmployeeNoLinkedAccount);
    }

    if (_currentUser.CallerEmployeeId.Value != targetEmployeeId)
    {
      throw new ForbiddenAccessException(
          "Ta operacja jest dozwolona tylko na własnym profilu albo przez rolę Owner/Manager.",
          ErrorCodes.EmployeeCannotMutateOtherProfile);
    }
  }

  private Guid RequireCallerEmployeeId() =>
    _currentUser.CallerEmployeeId
      ?? throw new ForbiddenAccessException(
          "Brak powiązania konta z pracownikiem w tym salonie.",
          ErrorCodes.EmployeeNoLinkedAccount);

  private async Task<StaffCalendarVisibilityPolicy> GetPolicyAsync(CancellationToken ct)
  {
    // Poza żądaniem HTTP (hosted service, własny scope) nie ma tenanta. Milczące przepuszczenie
    // byłoby luką — rzucamy, tak jak `TenantHandler` przy braku nagłówka tenanta.
    var tenantId = _currentTenant.TenantId
      ?? throw new NoTenantHeader();

    if (_cachedPolicy is { } cached && cached.TenantId == tenantId)
    {
      return cached.Policy;
    }

    var policy = await _context.Tenants
      .AsNoTracking()
      .Where(t => t.Id == tenantId)
      .Select(t => t.StaffCalendarVisibilityPolicy)
      .FirstOrDefaultAsync(ct);

    _cachedPolicy = (tenantId, policy);
    return policy;
  }
}
