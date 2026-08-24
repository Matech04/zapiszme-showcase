using App.Application.Admin.SmsTemplates.Commands.ApproveSmsTemplate;
using App.Application.Admin.SmsTemplates.Commands.RejectSmsTemplate;
using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Application.Notifications.SmsTemplates.Commands.SubmitSmsTemplate;
using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Notifications.Sms;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>
/// SMS-TPL-RESOLVE-* — rozwiązywanie zatwierdzonego szablonu, fallback przy braku/odrzuceniu, izolacja
/// tenantów i pełny obieg salon→admin przez handlery na realnym (InMemory) DbContext.
/// </summary>
public sealed class SmsTemplateResolverAndModerationTests
{
  private static readonly DateTime Now = new(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);

  private static ApplicationDbContext NewDb(Guid? tenantId, string dbName) =>
    new(
      new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options,
      new FakeCurrentTenantService(tenantId));

  private static NotificationPayload Payload(string salon = "Studio Ann") => new(
    SalonName: salon,
    CustomerName: "Anna",
    ServiceName: "Manicure",
    Date: new DateOnly(2026, 6, 25),
    StartTime: new TimeOnly(14, 30));

  private sealed class FakeModerationNotifier : ISmsTemplateModerationNotifier
  {
    public int Calls { get; private set; }
    public NotificationType? LastType { get; private set; }
    public string? LastBody { get; private set; }

    public Task NotifySubmittedForApprovalAsync(
      string salonName, NotificationType type, string proposedBody, CancellationToken ct = default)
    {
      Calls++;
      LastType = type;
      LastBody = proposedBody;
      return Task.CompletedTask;
    }
  }

  [Fact]
  public async Task Submit_TriggersModerationNotification()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(Submit_TriggersModerationNotification));
    var time = new FakeTimeProvider(Now);
    var notifier = new FakeModerationNotifier();

    var handler = new SubmitSmsTemplateCommandHandler(
      new FakeCurrentTenantService(tenant), db, time, notifier,
      NullLogger<SubmitSmsTemplateCommandHandler>.Instance);

    await handler.Handle(
      new SubmitSmsTemplateCommand(NotificationType.BookingConfirmationToCustomer, "Czesc {klient}!"), ct);

    // Po zgłoszeniu szablonu do akceptacji idzie powiadomienie do moderatora platformy.
    Assert.Equal(1, notifier.Calls);
    Assert.Equal(NotificationType.BookingConfirmationToCustomer, notifier.LastType);
    Assert.Equal("Czesc {klient}!", notifier.LastBody);
  }

  [Fact]
  public async Task Resolver_ApprovedTemplate_ReturnsRenderedSanitizedText()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(Resolver_ApprovedTemplate_ReturnsRenderedSanitizedText));

    var template = SmsTemplate.Create(tenant, NotificationType.BookingConfirmationToCustomer);
    template.SubmitForApproval("Zażółć {salon}: {data} {godzina}", Now);
    template.Approve(Now);
    db.SmsTemplates.Add(template);
    await db.SaveChangesAsync(ct);

    var resolver = new SmsTemplateResolver(db);
    var text = await resolver.TryResolveAsync(tenant, NotificationType.BookingConfirmationToCustomer, Payload(), ct);

    // Custom szablon ZACHOWUJE polskie znaki (świadomy wybór salonu, SMS leci UCS-2) — bez transliteracji.
    Assert.Equal("Zażółć Studio Ann: 25.06 14:30", text);
  }

  [Fact]
  public async Task Resolver_PendingTemplate_FallsBackToNull()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(Resolver_PendingTemplate_FallsBackToNull));

    var template = SmsTemplate.Create(tenant, NotificationType.BookingConfirmationToCustomer);
    template.SubmitForApproval("Niezatwierdzona {salon}", Now); // PendingApproval, Body == null
    db.SmsTemplates.Add(template);
    await db.SaveChangesAsync(ct);

    var resolver = new SmsTemplateResolver(db);
    var text = await resolver.TryResolveAsync(tenant, NotificationType.BookingConfirmationToCustomer, Payload(), ct);

    Assert.Null(text); // → kanał użyje hardcoded SmsTextBuilder
  }

  [Fact]
  public async Task Resolver_RejectedWithoutPriorApproved_FallsBackToNull()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(Resolver_RejectedWithoutPriorApproved_FallsBackToNull));

    var template = SmsTemplate.Create(tenant, NotificationType.BookingConfirmationToCustomer);
    template.SubmitForApproval("Spam {salon}", Now);
    template.Reject("zly", Now);
    db.SmsTemplates.Add(template);
    await db.SaveChangesAsync(ct);

    var resolver = new SmsTemplateResolver(db);
    var text = await resolver.TryResolveAsync(tenant, NotificationType.BookingConfirmationToCustomer, Payload(), ct);

    Assert.Null(text);
  }

  [Fact]
  public async Task Resolver_DoesNotLeakAcrossTenants()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var db = NewDb(tenantA, nameof(Resolver_DoesNotLeakAcrossTenants));

    var a = SmsTemplate.Create(tenantA, NotificationType.BookingConfirmationToCustomer);
    a.SubmitForApproval("Tenant A {salon}", Now);
    a.Approve(Now);
    db.SmsTemplates.Add(a);
    await db.SaveChangesAsync(ct);

    var resolver = new SmsTemplateResolver(db);
    // Tenant B nie ma szablonu → null, mimo że A ma zatwierdzony dla tego samego typu.
    var textB = await resolver.TryResolveAsync(tenantB, NotificationType.BookingConfirmationToCustomer, Payload(), ct);
    Assert.Null(textB);

    var textA = await resolver.TryResolveAsync(tenantA, NotificationType.BookingConfirmationToCustomer, Payload("S"), ct);
    Assert.Equal("Tenant A S", textA);
  }

  [Fact]
  public async Task Resolver_NonCustomizableType_AlwaysNull()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(Resolver_NonCustomizableType_AlwaysNull));
    var resolver = new SmsTemplateResolver(db);

    var text = await resolver.TryResolveAsync(tenant, NotificationType.NewBookingToSalon, Payload(), ct);
    Assert.Null(text);
  }

  [Fact]
  public async Task FullFlow_SubmitThenApprove_MakesTemplateResolvable()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var db = NewDb(tenant, nameof(FullFlow_SubmitThenApprove_MakesTemplateResolvable));
    var time = new FakeTimeProvider(Now);

    // Salon zgłasza (tenant-scoped handler).
    var submit = new SubmitSmsTemplateCommandHandler(
      new FakeCurrentTenantService(tenant), db, time, new FakeModerationNotifier(),
      NullLogger<SubmitSmsTemplateCommandHandler>.Instance);
    await submit.Handle(new SubmitSmsTemplateCommand(NotificationType.BookingConfirmationToCustomer, "{salon} potwierdza {data}"), ct);

    var pending = await db.SmsTemplates.IgnoreQueryFilters().SingleAsync(ct);
    Assert.Equal(SmsTemplateStatus.PendingApproval, pending.Status);

    // Admin zatwierdza (cross-tenant handler, brak ambient tenanta).
    var adminDb = NewDb(null, nameof(FullFlow_SubmitThenApprove_MakesTemplateResolvable));
    var approve = new ApproveSmsTemplateCommandHandler(adminDb, time);
    await approve.Handle(new ApproveSmsTemplateCommand(pending.Id), ct);

    var resolver = new SmsTemplateResolver(NewDb(tenant, nameof(FullFlow_SubmitThenApprove_MakesTemplateResolvable)));
    var text = await resolver.TryResolveAsync(tenant, NotificationType.BookingConfirmationToCustomer, Payload("S"), ct);
    Assert.Equal("S potwierdza 25.06", text);
  }

  [Fact]
  public async Task Admin_Reject_KeepsPreviousApprovedActive()
  {
    var ct = TestContext.Current.CancellationToken;
    var tenant = Guid.NewGuid();
    var dbName = nameof(Admin_Reject_KeepsPreviousApprovedActive);
    var time = new FakeTimeProvider(Now);

    // Pierwsza treść zatwierdzona.
    var t = SmsTemplate.Create(tenant, NotificationType.BookingConfirmationToCustomer);
    t.SubmitForApproval("Pierwsza {salon}", Now);
    t.Approve(Now);
    var seedDb = NewDb(tenant, dbName);
    seedDb.SmsTemplates.Add(t);
    await seedDb.SaveChangesAsync(ct);

    // Salon edytuje → pending; admin odrzuca.
    var submitDb = NewDb(tenant, dbName);
    await new SubmitSmsTemplateCommandHandler(
        new FakeCurrentTenantService(tenant), submitDb, time, new FakeModerationNotifier(),
        NullLogger<SubmitSmsTemplateCommandHandler>.Instance)
      .Handle(new SubmitSmsTemplateCommand(NotificationType.BookingConfirmationToCustomer, "Druga {salon}"), ct);

    var adminDb = NewDb(null, dbName);
    await new RejectSmsTemplateCommandHandler(adminDb, time)
      .Handle(new RejectSmsTemplateCommand(t.Id, "Nie pasuje"), ct);

    // Poprzednia zatwierdzona treść nadal aktywna przy wysyłce.
    var resolver = new SmsTemplateResolver(NewDb(tenant, dbName));
    var text = await resolver.TryResolveAsync(tenant, NotificationType.BookingConfirmationToCustomer, Payload("S"), ct);
    Assert.Equal("Pierwsza S", text);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid? tenantId) => TenantId = tenantId;
  }

  private sealed class FakeTimeProvider : TimeProvider
  {
    private readonly DateTimeOffset _now;
    public FakeTimeProvider(DateTime nowUtc) => _now = new DateTimeOffset(nowUtc);
    public override DateTimeOffset GetUtcNow() => _now;
  }
}
