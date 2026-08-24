using App.Application.Common.Security;

namespace App.Application.UnitTests.Support;

/// <summary>
/// Atrapa <see cref="IStaffAccessPolicy"/>, która przepuszcza WSZYSTKO. Do testów handlerów, które
/// weryfikują logikę domenową, nie autoryzację — tę pokrywają `StaffAccessPolicyTests`
/// (jednostkowo) i `AppointmentAuthorizationMatrixIntegrationTests` (przez HTTP).
///
/// Liczniki pozwalają sprawdzić, że handler w ogóle O COŚ zapytał — bez tego test „handler działa"
/// przeszedłby także wtedy, gdyby ktoś usunął strażnika.
/// </summary>
internal sealed class PermissiveStaffAccessPolicy : IStaffAccessPolicy
{
  public List<Guid> ViewedEmployeeIds { get; } = [];
  public List<Guid> MutatedEmployeeIds { get; } = [];
  public List<Guid> MutatedAppointmentIds { get; } = [];
  public List<Guid> ProfileChecks { get; } = [];

  public Task EnsureCanViewEmployeeCalendarAsync(Guid employeeId, CancellationToken ct)
  {
    ViewedEmployeeIds.Add(employeeId);
    return Task.CompletedTask;
  }

  public Task EnsureCanMutateEmployeeCalendarAsync(Guid employeeId, CancellationToken ct)
  {
    MutatedEmployeeIds.Add(employeeId);
    return Task.CompletedTask;
  }

  public Task EnsureCanMutateAppointmentAsync(Guid appointmentId, CancellationToken ct)
  {
    MutatedAppointmentIds.Add(appointmentId);
    return Task.CompletedTask;
  }

  public Task EnsureCanReadEmployeeCalendarDataAsync(Guid employeeId, CancellationToken ct)
  {
    ViewedEmployeeIds.Add(employeeId);
    return Task.CompletedTask;
  }

  public Task<Guid?> ResolveCalendarReadScopeAsync(Guid? requestedEmployeeId, CancellationToken ct) =>
    Task.FromResult(requestedEmployeeId);

  public void EnsureSelfOrStaffManager(Guid targetEmployeeId) => ProfileChecks.Add(targetEmployeeId);
}
