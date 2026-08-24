using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.E2eSupport;

public sealed record RestApiIntegrationSeedResult(
  Guid TenantId,
  string TenantSlug,
  Guid EmployeeId,
  Guid ServiceId,
  Guid ServiceCategoryId,
  Guid VatRateId,
  Guid CustomerId);

/// <summary>Minimalny zestaw danych pod testy panelu i publicznego katalogu booking.</summary>
public static class RestApiIntegrationSeed
{
  public const string TenantSlug = "rest-api-integration";
  public const string SecondTenantSlug = "rest-api-integration-second";
  private static readonly string[] Roles = ["Admin", "Owner", "Manager", "Employee"];

  public static RestApiIntegrationSeedResult Seed(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    SeedIdentity(db);

    var seeded = BuildTenantAggregate(
      ownerUserId: IntegrationTestUserIds.SalonOwner,
      tenantName: "REST API Seed Salon",
      tenantSlug: TenantSlug,
      ownerEmail: "owner@rest-seed.local",
      customerEmail: "jan@rest-seed.local",
      customerPhone: "+48501110001");

    db.Tenants.Add(seeded.tenant);
    db.ServiceCategories.Add(seeded.category);
    db.VatRates.Add(seeded.vat);
    db.Employees.Add(seeded.employee);
    db.Services.Add(seeded.service);
    db.Customers.Add(seeded.customer);
    db.SaveChanges();

    return new RestApiIntegrationSeedResult(
        seeded.tenant.Id,
        TenantSlug,
        seeded.employee.Id,
        seeded.service.Id,
        seeded.category.Id,
        seeded.vat.Id,
        seeded.customer.Id);
  }

  public static RestApiIntegrationSeedResult SeedSecondTenant(IServiceProvider rootServices)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    SeedIdentity(db);

    var seeded = BuildTenantAggregate(
      ownerUserId: IntegrationTestUserIds.SecondSalonOwner,
      tenantName: "REST API Seed Salon 2",
      tenantSlug: SecondTenantSlug,
      ownerEmail: "owner2@rest-seed.local",
      customerEmail: "jan2@rest-seed.local",
      customerPhone: "+48501110002");

    db.Tenants.Add(seeded.tenant);
    db.ServiceCategories.Add(seeded.category);
    db.VatRates.Add(seeded.vat);
    db.Employees.Add(seeded.employee);
    db.Services.Add(seeded.service);
    db.Customers.Add(seeded.customer);
    db.SaveChanges();

    return new RestApiIntegrationSeedResult(
      seeded.tenant.Id,
      SecondTenantSlug,
      seeded.employee.Id,
      seeded.service.Id,
      seeded.category.Id,
      seeded.vat.Id,
      seeded.customer.Id);
  }

  private static void SeedIdentity(ApplicationDbContext db)
  {
    foreach (var roleName in Roles)
    {
      var normalizedName = roleName.ToUpperInvariant();
      if (!db.Roles.Any(r => r.NormalizedName == normalizedName))
      {
        db.Roles.Add(new IdentityRole<Guid>
        {
          Id = Guid.NewGuid(),
          Name = roleName,
          NormalizedName = normalizedName,
          ConcurrencyStamp = Guid.NewGuid().ToString(),
        });
      }
    }

    db.SaveChanges();

    AddUserIfMissing(db, IntegrationTestUserIds.SalonOwner, "owner@rest-seed.local", "Ann Owner", "Owner");
    AddUserIfMissing(db, IntegrationTestUserIds.SecondSalonOwner, "owner2@rest-seed.local", "Beth Owner", "Owner");
    AddUserIfMissing(db, IntegrationTestUserIds.SystemAdmin, "admin@rest-seed.local", "System Admin", "Admin");
  }

  private static (Tenant tenant, ServiceCategory category, VatRate vat, Employee employee, Service service, Customer customer) BuildTenantAggregate(
    Guid ownerUserId,
    string tenantName,
    string tenantSlug,
    string ownerEmail,
    string customerEmail,
    string customerPhone)
  {
    var tenant = new Tenant(tenantName, tenantSlug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email);

    var category = new ServiceCategory(tenant.Id, "Kategoria", 0);
    var vat = new VatRate(tenant.Id, "VAT 23%", 0.23m);
    var employee = new Employee(tenant.Id, ownerUserId, "Ann", "Owner", ownerEmail);

    var dayRanges = (IReadOnlyCollection<TimeRange>)new List<TimeRange>
    {
      new(new TimeOnly(8, 0), new TimeOnly(20, 0)),
    };
    var weekly = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => dayRanges);
    employee.SetWeeklySchedule(weekly);

    var service = new Service(tenant.Id, category.Id, vat.Id, "Strzyżenie", new Money(80m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(service.Price.Amount, service.Price.Currency));
    var customer = new Customer(tenant.Id, "Jan", "Kowalski", customerEmail, new PhoneNumber(customerPhone), "");

    return (tenant, category, vat, employee, service, customer);
  }

  private static void AddUserIfMissing(
    ApplicationDbContext db,
    Guid userId,
    string email,
    string displayName,
    string roleName)
  {
    if (db.Users.Any(u => u.Id == userId))
    {
      return;
    }

    var user = new User(email, displayName)
    {
      Id = userId,
      EmailConfirmed = true,
      // Po dodaniu phone OTP gate seed-users muszą mieć też PhoneNumberConfirmed=true,
      // inaczej testy logowania padają (login wymaga obu potwierdzeń).
      PhoneNumberConfirmed = true,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password123!");

    var roleId = db.Roles
      .Where(r => r.NormalizedName == roleName.ToUpperInvariant())
      .Select(r => r.Id)
      .Single();

    db.Users.Add(user);
    db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
    db.SaveChanges();
  }
}
