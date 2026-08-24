using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.GlobalSettingsAggregate;
using App.Domain.Aggregates.ImpersonationAggregate;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.ShortLinkAggregate;
using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>, IApplicationDbContext, IUnitOfWork
{

  private readonly ICurrentTenantService _currentTenantService;

  //constructor takes options
  public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService currentTenantService) : base(options)
  {
    _currentTenantService = currentTenantService;
  }
  // Implementing Tables from interface
  public DbSet<Tenant> Tenants => Set<Tenant>();
  public DbSet<Employee> Employees => Set<Employee>();
  public DbSet<Customer> Customers => Set<Customer>();
  public DbSet<Service> Services => Set<Service>();
  public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
  public override DbSet<User> Users { get; set; } = null!;

  // Bez HasQueryFilter — encja jest kluczowana użytkownikiem, nie salonem (patrz komentarz
  // w UserGuideCompletion). Izolację zapewnia filtr po UserId z claima w handlerach.
  public DbSet<UserGuideCompletion> UserGuideCompletions => Set<UserGuideCompletion>();
  public DbSet<VatRate> VatRates => Set<VatRate>();
  public DbSet<Appointment> Appointments => Set<Appointment>();
  public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
  public DbSet<SelfServiceOtp> SelfServiceOtps => Set<SelfServiceOtp>();
  public DbSet<PhoneConfirmationOtp> PhoneConfirmationOtps => Set<PhoneConfirmationOtp>();
  public DbSet<Notification> Notifications => Set<Notification>();
  public DbSet<NotificationUsageRecord> NotificationUsage => Set<NotificationUsageRecord>();
  public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
  public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
  public DbSet<PromoCodeRedemption> PromoCodeRedemptions => Set<PromoCodeRedemption>();
  public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();
  public DbSet<ShortLink> ShortLinks => Set<ShortLink>();
  public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
  // Encja PLATFORMOWA (singleton), BEZ query filtra — jak PromoCode / ImpersonationSession.
  public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    //Runs configuration from parent class after this method we can add our own
    base.OnModelCreating(modelBuilder);

    // Searches for all classes that implement IEntityTypeConfiguration (Fluent Api Mapping)
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

    modelBuilder.Entity<Employee>()
      .HasQueryFilter(e => e.TenantId == _currentTenantService.TenantId && e.IsActive);

    modelBuilder.Entity<Customer>()
      .HasQueryFilter(c => c.TenantId == _currentTenantService.TenantId && c.IsActive);

    modelBuilder.Entity<Service>()
      .HasQueryFilter(s => s.TenantId == _currentTenantService.TenantId && s.IsActive);

    modelBuilder.Entity<ServiceCategory>()
      .HasQueryFilter(sc => sc.TenantId == _currentTenantService.TenantId && sc.IsActive);

    modelBuilder.Entity<VatRate>()
      .HasQueryFilter(v => v.TenantId == _currentTenantService.TenantId);

    modelBuilder.Entity<Appointment>()
      .HasQueryFilter(a => a.TenantId == _currentTenantService.TenantId);

    modelBuilder.Entity<ShiftTemplate>()
      .HasQueryFilter(t => t.TenantId == _currentTenantService.TenantId);

    modelBuilder.Entity<Notification>()
      .HasQueryFilter(n => n.TenantId == _currentTenantService.TenantId);

    modelBuilder.Entity<NotificationUsageRecord>()
      .HasQueryFilter(u => u.TenantId == _currentTenantService.TenantId);

    // PushSubscription — subskrypcje Web Push urządzeń pracowników; tenant-scoped.
    modelBuilder.Entity<PushSubscription>()
      .HasQueryFilter(s => s.TenantId == _currentTenantService.TenantId);

    // SelfServiceOtp jest ITenantEntity (hash OTP + token sesji + kontakt klienta self-service).
    // Bez tego filtra odczyt bez ręcznego Where(TenantId) leakuje OTP/sesje innych salonów —
    // dotąd chroniony tylko ręcznymi filtrami w handlerach (kruche). Teraz egzekwowane warstwowo.
    modelBuilder.Entity<SelfServiceOtp>()
      .HasQueryFilter(o => o.TenantId == _currentTenantService.TenantId);

    // PromoCodeRedemption — tenant-scoped (świadoma decyzja: filter włączony).
    // UWAGA: PromoCode nie ma query filtra — jest GLOBAL (świadomy wyjątek od reguły izolacji tenantów;
    // każdy tenant ma dostęp do tych samych kodów, lookup po Code bez kontekstu tenant'a).
    modelBuilder.Entity<PromoCodeRedemption>()
      .HasQueryFilter(r => r.TenantId == _currentTenantService.TenantId);

    // SmsTemplate — tenant-scoped. Salon czyta/zapisuje swoje szablony przez ten filtr.
    // UWAGA: adminowy przegląd kolejki moderacji (cross-tenant) ORAZ resolver przy wysyłce
    // (background jobs, brak ambient tenanta) świadomie używają IgnoreQueryFilters z jawnym
    // Where(TenantId == ...) — izolacja zachowana przez jawny filtr.
    modelBuilder.Entity<SmsTemplate>()
      .HasQueryFilter(t => t.TenantId == _currentTenantService.TenantId);

    // ImpersonationSession — encja PLATFORMOWA (support impersonation), BEZ query filtra
    // (świadomy wyjątek od reguły izolacji tenantów, jak PromoCode): to dziennik dostępu supportu,
    // nie dane tenanta. Czytana/zapisywana ponad kontekstem tenanta.
  }

  //Guard for tenants
  public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
  {
    // Pobieramy aktualne ID zalogowanego użytkownika/tenanta
    var currentTenantId = _currentTenantService.TenantId;

    foreach (var entry in ChangeTracker.Entries<ITenantEntity>()) // <--- Magia interfejsu!
    {
      switch (entry.State)
      {
        case EntityState.Added:
        case EntityState.Modified:
        // Deleted też pokrywamy: hard-delete encji nie-ISoftDelete (np. Appointment) omijał
        // dotąd guard — handler bez ręcznego checku mógłby skasować cudzą encję bez TenantViolation.
        case EntityState.Deleted:
          // SPRAWDZENIE BEZPIECZEŃSTWA
          // Jeśli ktoś próbuje zapisać/usunąć obiekt z innym TenantId niż ten,
          // w którego kontekście działa żądanie -> BLOKUJEMY.
          // UWAGA: gdy currentTenantId == null (rejestracja owner'a tworzy Tenant+Employee bez
          // kontekstu tenanta; background jobs operują cross-tenant z IgnoreQueryFilters) check
          // jest świadomie pomijany — fail-open na zapisie bez kontekstu to celowy wyjątek, nie luka.
          if (currentTenantId.HasValue && entry.Entity.TenantId != currentTenantId.Value)
          {
            // To jest sytuacja krytyczna - próba naruszenia izolacji danych!
            throw new TenantViolation();
          }
          break;
      }
    }


    return base.SaveChangesAsync(cancellationToken);
  }

  public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
  {
    // InMemory (testy jednostkowe) nie wspiera transakcji — uruchamiamy akcję wprost.
    if (!Database.IsRelational())
    {
      await action(ct);
      return;
    }

    var strategy = Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
      await using var transaction = await Database.BeginTransactionAsync(ct);
      await action(ct);
      await transaction.CommitAsync(ct);
    });
  }

}