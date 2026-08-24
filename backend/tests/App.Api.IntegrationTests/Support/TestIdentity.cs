using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Api.IntegrationTests;

/// <summary>
/// Zakłada wiersz w <c>Users</c> dla tożsamości, którą test przypina do <c>Employee</c>.
///
/// Dlaczego to w ogóle potrzebne: <c>IntegrationTestAuthenticationHandler</c> FABRYKUJE tożsamość
/// z nagłówków i nigdy nie zagląda do bazy, więc testy przywykły podawać `userId` jako gołe GUID-y
/// (albo stałe z <c>IntegrationTestUserIds</c>) bez zakładania konta. Na InMemory przechodziło to
/// bez słowa — ten provider NIE egzekwuje kluczy obcych. Na prawdziwym Postgresie kończy się
/// `FK_Employees_Users_user_id` i wywracało 21 testów.
///
/// Świadomie jest to helper wołany PUNKTOWO, a nie globalny seed do bazy-szablonu: wersja globalna
/// (sprawdzona) naprawiała klucz obcy, ale dokładała konta do KAŻDEJ bazy i psuła testy autoryzacji,
/// które liczą użytkowników albo zakładają, że dany e-mail jest wolny.
/// </summary>
internal static class TestIdentity
{
  /// <summary>
  /// Idempotentnie zapewnia konto o podanym <paramref name="userId"/>. Zwraca to samo id,
  /// żeby dało się wołać w miejscu argumentu: <c>new Employee(tenantId, TestIdentity.Ensure(db, id, mail), …)</c>.
  /// </summary>
  public static Guid Ensure(ApplicationDbContext db, Guid userId, string email)
  {
    if (db.Users.IgnoreQueryFilters().Any(u => u.Id == userId))
    {
      return userId;
    }

    db.Users.Add(new User(email, email)
    {
      Id = userId,
      EmailConfirmed = true,
      PhoneNumber = "+48500000000",
      PhoneNumberConfirmed = true,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    });
    db.SaveChanges();

    return userId;
  }
}
