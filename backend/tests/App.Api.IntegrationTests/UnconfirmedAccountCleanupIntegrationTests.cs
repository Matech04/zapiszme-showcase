using App.Api.E2eSupport;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Api.IntegrationTests;

/// <summary>
/// AUTH-CLEANUP-INT-001..002 — cykl <see cref="UnconfirmedAccountCleanupHostedService"/> na
/// REALNYM Postgresie (Testcontainers). Testy jednostkowe stoją na EF InMemory, które NIE
/// egzekwuje FK ani reguły SET NULL — a to właśnie ukrywało pierwotny błąd (kasowanie żywych
/// salonów dławiące się o FK RESTRICT). Tu weryfikujemy zachowanie tam, gdzie FK naprawdę działa:
///
/// (001) Niepotwierdzone zaproszenie pracownika w PUSTYM salonie (brak wizyt / klientów / usług,
///       więc BEZ blokady FK RESTRICT) — mimo to salon właściciela MUSI przetrwać dzięki bramce
///       „pusta skorupa = dokładnie jeden pracownik". To odróżnia właściwą naprawę od przypadkowej
///       ochrony przez FK: gdyby liczyła tylko blokada FK, ten pusty tenant zostałby skasowany.
///
/// (002) Porzucony self-signup (pusty tenant, jeden pracownik, e-mail niepotwierdzony) — MUSI
///       zostać skasowany czysto, bez wyjątku FK. Dowód, że legalna ścieżka wciąż działa na Postgresie.
/// </summary>
public sealed class UnconfirmedAccountCleanupIntegrationTests
{
  private static readonly TimeSpan Grace = TimeSpan.FromHours(48);
  private static readonly DateTime UtcNow = new(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc);

  [Fact]
  public async Task Unaccepted_invite_in_empty_salon_does_not_delete_owner_tenant_on_real_postgres()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    Guid tenantId, ownerUserId, ownerEmployeeId, inviteeUserId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // PUSTY salon: właściciel + jego pracownik, ale bez wizyt/klientów/usług — czyli BEZ
      // żadnych wierszy z FK RESTRICT do Tenants. Bez naprawy kasowanie by przeszło.
      var tenant = new Tenant("Pusty salon", "pusty-salon-invite");
      var owner = NewUser("owner-empty@e.local", emailConfirmed: true, phoneNumber: "+48500100300", phoneConfirmed: true, createdAt: UtcNow - TimeSpan.FromDays(30));
      var ownerEmployee = new Employee(tenant.Id, owner.Id, "Ann", "Owner", "owner-empty@e.local");

      // Niepotwierdzone zaproszenie: e-mail niepotwierdzony, telefonu brak. Wskazuje przez
      // Employee na tenant właściciela — to drugi pracownik tego salonu.
      var invitee = NewUser("invitee@e.local", emailConfirmed: false, phoneNumber: null, phoneConfirmed: false, createdAt: UtcNow - TimeSpan.FromHours(72));
      var inviteeEmployee = new Employee(tenant.Id, invitee.Id, "Iva", "Invitee", "invitee@e.local");

      db.Tenants.Add(tenant);
      db.Users.AddRange(owner, invitee);
      db.Employees.AddRange(ownerEmployee, inviteeEmployee);
      await db.SaveChangesAsync(ct);

      tenantId = tenant.Id;
      ownerUserId = owner.Id;
      ownerEmployeeId = ownerEmployee.Id;
      inviteeUserId = invitee.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, UtcNow, Grace, ct, NullLogger.Instance);
    }

    using var verify = factory.Services.CreateScope();
    var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.True(await vdb.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct), "Salon właściciela MUSI przetrwać");
    Assert.True(await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == ownerUserId, ct), "Właściciel MUSI przetrwać");
    Assert.True(await vdb.Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == ownerEmployeeId, ct), "Pracownik-właściciel MUSI przetrwać");
    Assert.False(await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == inviteeUserId, ct), "Porzucone zaproszenie MA zniknąć");

    // Salon zachowuje rekord pracownika zaproszenia jako zasób — na produkcji odczepiony od
    // skasowanego usera przez FK user_id = SET NULL (zweryfikowane na realnej bazie; tu pomijamy
    // asercję na UserId, bo współdzielony DbContext fabryki zwraca prześledzony, nieodświeżony wiersz).
    Assert.True(await vdb.Employees.IgnoreQueryFilters()
      .AnyAsync(e => e.TenantId == tenantId && e.Email == "invitee@e.local", ct), "Rekord pracownika zaproszenia MA zostać");
  }

  [Fact]
  public async Task Abandoned_self_signup_empty_tenant_is_deleted_cleanly_on_real_postgres()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    Guid tenantId, userId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      var tenant = new Tenant("Porzucony salon", "porzucony-salon");
      var user = NewUser("abandoned@e.local", emailConfirmed: false, phoneNumber: "+48500100400", phoneConfirmed: false, createdAt: UtcNow - TimeSpan.FromHours(72));
      var employee = new Employee(tenant.Id, user.Id, "Abe", "Abandoned", "abandoned@e.local");

      db.Tenants.Add(tenant);
      db.Users.Add(user);
      db.Employees.Add(employee);
      await db.SaveChangesAsync(ct);

      tenantId = tenant.Id;
      userId = user.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, UtcNow, Grace, ct, NullLogger.Instance);
    }

    using var verify = factory.Services.CreateScope();
    var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, ct), "Porzucony self-signup MA zniknąć");
    Assert.False(await vdb.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct), "Pusty tenant MA zniknąć razem z kontem");
    Assert.False(await vdb.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId, ct), "Pracownik pustego tenanta MA zniknąć");
  }

  // (003) Kryterium #2 (Droga B) — sierota kreatora: e-mail I telefon potwierdzone, ale konto
  //       porzuciło kreator PRZED krokiem „profil", więc nigdy nie dostało rekordu Employee (a więc
  //       i tenanta). Kryterium #1 tego nie łapie (obie flagi true). Cleanup MA usunąć osierocone
  //       konto. Dowód, że nowe kryterium działa i nie potrzebuje żadnego tenanta do skasowania.
  [Fact]
  public async Task Confirmed_account_without_employee_is_deleted_as_abandoned_wizard_orphan()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    Guid orphanUserId, freshUserId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // Sierota: potwierdzony e-mail+telefon, brak Employee, starszy niż grace window.
      var orphan = NewUser("orphan-wizard@e.local", emailConfirmed: true, phoneNumber: "+48500100500", phoneConfirmed: true, createdAt: UtcNow - TimeSpan.FromHours(72));

      // Świeża sierota (w grace window) — jeszcze NIE do skasowania, chroni przed kasowaniem
      // usera, który właśnie potwierdził telefon i za chwilę kliknie „Utwórz salon".
      var fresh = NewUser("orphan-fresh@e.local", emailConfirmed: true, phoneNumber: "+48500100501", phoneConfirmed: true, createdAt: UtcNow - TimeSpan.FromHours(1));

      db.Users.AddRange(orphan, fresh);
      await db.SaveChangesAsync(ct);

      orphanUserId = orphan.Id;
      freshUserId = fresh.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, UtcNow, Grace, ct, NullLogger.Instance);
    }

    using var verify = factory.Services.CreateScope();
    var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.False(await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == orphanUserId, ct), "Porzucona sierota kreatora MA zniknąć");
    Assert.True(await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == freshUserId, ct), "Świeże konto w grace window MUSI przetrwać");
  }

  // (004) Regresja z preflightu 2026-07-31, severity CRITICAL. Kryterium #2 zakładało w komentarzu,
  //       że administratora platformy chroni brak potwierdzonego telefonu. Na produkcji konto
  //       `kontakt@zapisz.me` miało PhoneNumberConfirmed = true przy PhoneNumber = NULL i BEZ rekordu
  //       Employee — czyli kwalifikowało się do twardego skasowania w pierwszym cyklu po deployu.
  //       Zapytanie na żywej bazie zwróciło dokładnie 1 trafienie.
  //
  //       Test odtwarza dokładnie ten kształt danych. Przechodzi na obu providerach, ale WARTOŚĆ
  //       ma dopiero na Postgresie (`INTEGRATION_DB_PROVIDER=Postgres`): bramka to podzapytanie po
  //       AspNetUserRoles ↔ AspNetRoles, a błąd tłumaczenia na SQL ujawni się wyłącznie tam —
  //       InMemory wykona ten sam LINQ w pamięci i przepuści konstrukcję nietłumaczalną przez Npgsql.
  //       Zweryfikowane na obu.
  [Fact]
  public async Task Platform_admin_without_employee_is_never_deleted()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    Guid adminUserId, orphanUserId;

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

      // Kształt 1:1 z produkcji: telefon POTWIERDZONY, ale sam numer pusty; brak Employee.
      var admin = NewUser(
        "kontakt@zapisz.me",
        emailConfirmed: true,
        phoneNumber: null,
        phoneConfirmed: true,
        createdAt: UtcNow - TimeSpan.FromDays(70));

      // Kontrola: zwykła sierota kreatora obok admina MA zostać skasowana. Bez tego test
      // przechodziłby także wtedy, gdyby ktoś wyłączył całe kryterium #2.
      var orphan = NewUser(
        "orphan-control@e.local",
        emailConfirmed: true,
        phoneNumber: "+48500100600",
        phoneConfirmed: true,
        createdAt: UtcNow - TimeSpan.FromHours(72));

      db.Users.AddRange(admin, orphan);
      await db.SaveChangesAsync(ct);

      var adminRole = new IdentityRole<Guid>(UnconfirmedAccountCleanupHostedService.PlatformAdminRole)
      {
        Id = Guid.NewGuid(),
        NormalizedName = UnconfirmedAccountCleanupHostedService.PlatformAdminRole.ToUpperInvariant(),
      };
      db.Roles.Add(adminRole);
      db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = admin.Id, RoleId = adminRole.Id });
      await db.SaveChangesAsync(ct);

      adminUserId = admin.Id;
      orphanUserId = orphan.Id;
    }

    using (var runScope = factory.Services.CreateScope())
    {
      var db = runScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      await UnconfirmedAccountCleanupHostedService.RunCycleAsync(db, UtcNow, Grace, ct, NullLogger.Instance);
    }

    using var verify = factory.Services.CreateScope();
    var vdb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    Assert.True(
      await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == adminUserId, ct),
      "Konto administratora platformy NIE MOŻE zostać skasowane przez cleanup");
    Assert.False(
      await vdb.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == orphanUserId, ct),
      "Kontrola: zwykła sierota kreatora nadal MA być kasowana");
  }

  private static User NewUser(string email, bool emailConfirmed, string? phoneNumber, bool phoneConfirmed, DateTime createdAt)
  {
    var user = new User(email, email)
    {
      EmailConfirmed = emailConfirmed,
      PhoneNumber = phoneNumber,
      PhoneNumberConfirmed = phoneConfirmed,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    typeof(User).GetProperty(nameof(User.CreatedAt))!.SetValue(user, createdAt);
    return user;
  }
}
