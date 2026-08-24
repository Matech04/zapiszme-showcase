using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments;

/// <summary>
/// Wylicza łączny czas trwania combo (suma czasów usług z per-pracownik override).
/// Pobiera wszystkie usługi JEDNYM zapytaniem (<c>Contains</c>) zamiast K round-tripów,
/// zachowując walidację kompletności: brak którejkolwiek żądanej usługi → <see cref="NotFoundException"/>.
/// </summary>
internal static class ComboDurationResolver
{
  public static async Task<int> ResolveAsync(
    IApplicationDbContext context,
    Employee employee,
    IReadOnlyList<Guid> serviceIds,
    CancellationToken ct)
  {
    if (serviceIds.Count == 0)
    {
      return 0;
    }

    var distinctIds = serviceIds.Distinct().ToList();

    var services = await context.Services
      .AsNoTracking()
      .Where(s => distinctIds.Contains(s.Id))
      .Select(s => new { s.Id, s.DurationInMinutes })
      .ToDictionaryAsync(s => s.Id, s => s.DurationInMinutes, ct);

    var total = 0;
    foreach (var serviceId in serviceIds)
    {
      if (!services.TryGetValue(serviceId, out var duration))
      {
        throw new NotFoundException(nameof(Service), serviceId);
      }

      total += employee.ResolveServiceDurationMinutes(serviceId, duration);
    }

    return total;
  }
}
