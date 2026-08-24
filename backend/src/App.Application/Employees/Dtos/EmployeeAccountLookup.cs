using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Employees.Dtos;

/// <summary>
/// Dołącza do pracowników informacje z warstwy Identity (rola konta + czy zaproszenie jeszcze
/// nieaktywowane). Role żyją w tabelach Identity, nie na aggregate <c>Employee</c>, więc czytamy je
/// jednym joinem po <c>UserId</c> zamiast per-pracownik (unikamy N+1).
/// </summary>
internal static class EmployeeAccountLookup
{
  public readonly record struct AccountInfo(string? Role, bool InvitePending);

  public static async Task<Dictionary<Guid, AccountInfo>> LoadAsync(
      IApplicationDbContext context,
      IReadOnlyCollection<Guid> userIds,
      CancellationToken ct)
  {
    if (userIds.Count == 0)
    {
      return new Dictionary<Guid, AccountInfo>();
    }

    var roleRows = await (
        from ur in context.Set<IdentityUserRole<Guid>>()
        join r in context.Set<IdentityRole<Guid>>() on ur.RoleId equals r.Id
        where userIds.Contains(ur.UserId)
        select new { ur.UserId, r.Name })
        .ToListAsync(ct);

    var emailConfirmedByUser = await context.Users
        .Where(u => userIds.Contains(u.Id))
        .Select(u => new { u.Id, u.EmailConfirmed })
        .ToDictionaryAsync(u => u.Id, u => u.EmailConfirmed, ct);

    var rolesByUser = roleRows
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToArray());

    return userIds.ToDictionary(
        id => id,
        id => new AccountInfo(
            Role: rolesByUser.TryGetValue(id, out var roles) ? Roles.PrimaryRole(roles) : null,
            InvitePending: emailConfirmedByUser.TryGetValue(id, out var confirmed) && !confirmed));
  }
}
