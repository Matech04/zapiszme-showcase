using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Services;

/// <summary>
/// Waliduje listę dodatków przypinanych do usługi głównej: każdy wskazany id musi istnieć w bieżącej
/// tenancy (HasQueryFilter automatycznie wycina inne tenanty i nieaktywne) oraz mieć
/// <c>IsAddon = true</c>; usługa nie może być własnym dodatkiem.
/// </summary>
public static class ServiceAddonValidator
{
  public static async Task EnsureAddonsValidAsync(
    IApplicationDbContext context,
    IReadOnlyCollection<Guid> addonServiceIds,
    Guid mainServiceId,
    CancellationToken ct)
  {
    var ids = addonServiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
    if (ids.Count == 0)
    {
      return;
    }

    if (ids.Contains(mainServiceId))
    {
      throw new ServiceAddonInvalidException("Usługa nie może być własnym dodatkiem.");
    }

    var found = await context.Services
      .Where(s => ids.Contains(s.Id))
      .Select(s => new { s.Id, s.IsAddon })
      .ToListAsync(ct);

    if (found.Count != ids.Count)
    {
      throw new ServiceAddonInvalidException("Wybrany dodatek nie istnieje.");
    }

    if (found.Any(s => !s.IsAddon))
    {
      throw new ServiceAddonInvalidException("Wskazana usługa nie jest dodatkiem.");
    }
  }
}
