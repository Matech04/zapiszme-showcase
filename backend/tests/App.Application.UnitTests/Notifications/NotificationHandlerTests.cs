using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Notifications.Events;
using App.Application.Notifications.Handlers;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Application.UnitTests.Notifications;

/// <summary>
/// NOTIF-CORE-* — handlery zdarzeń: rozsyłają wiadomości wg <see cref="NotificationSettings"/>
/// i są best-effort wobec błędów dispatchera.
/// </summary>
public sealed class NotificationHandlerTests
{
  [Fact]
  public async Task BookingConfirmed_AllEnabled_DispatchesToSalonAndCustomer()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    Assert.Equal(2, dispatcher.Messages.Count);
    var toSalon = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.NewBookingToSalon);
    Assert.Equal(fx.Employee.Email, toSalon.Recipient.Email);
    var toCustomer = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.BookingConfirmationToCustomer);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon), nie e-mail.
    Assert.Equal(fx.Customer.PhoneNumber!.Value, toCustomer.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(toCustomer.Recipient.Email));
    Assert.Equal(fx.Tenant.Name, toCustomer.Payload.SalonName);
  }

  [Fact]
  public async Task BookingConfirmed_SalonPayload_CarriesCustomerNameAndContact()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toSalon = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.NewBookingToSalon);
    Assert.Equal("Jan Kowalski", toSalon.Payload.CustomerFullName);
    Assert.Equal(fx.Customer.PhoneNumber!.Value, toSalon.Payload.CustomerPhone);
    Assert.Equal(fx.Customer.Email, toSalon.Payload.CustomerEmail);
  }

  [Fact]
  public async Task BookingConfirmed_CustomerWithoutName_SalonPayloadHasEmptyFullNameButContact()
  {
    // Salon nie wymaga nazwiska → klient podał tylko kontakt. Mail do salonu i tak ma pokazać
    // realny kontakt, a wiersz „Klient" zniknie (CustomerFullName puste).
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, customerEmail: string.Empty);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toSalon = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.NewBookingToSalon);
    Assert.True(string.IsNullOrEmpty(toSalon.Payload.CustomerFullName));
    Assert.Equal(fx.Customer.PhoneNumber!.Value, toSalon.Payload.CustomerPhone);
  }

  [Fact]
  public async Task BookingConfirmed_EmailChannel_SendsToCustomerEmail()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, verificationChannel: CustomerVerificationChannel.Email);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toCustomer = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.BookingConfirmationToCustomer);
    Assert.Equal(fx.Customer.Email, toCustomer.Recipient.Email);
    Assert.True(string.IsNullOrEmpty(toCustomer.Recipient.Phone));
  }

  [Fact]
  public async Task BookingConfirmed_SmsCustomerWithoutEmail_StillNotifiesViaPhone()
  {
    // Regresja: klient zweryfikowany SMS-em NIE ma e-maila — wcześniej handler wymagał e-maila
    // i pomijał wysyłkę. Teraz powiadomienie leci kanałem klienta (SMS).
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, customerEmail: string.Empty);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toCustomer = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.BookingConfirmationToCustomer);
    Assert.Equal(fx.Customer.PhoneNumber!.Value, toCustomer.Recipient.Phone);
  }

  [Fact]
  public async Task BookingConfirmed_CustomerConfirmationDisabled_DispatchesOnlyToSalon()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, settings: new NotificationSettings(
      newBookingToSalon: true,
      bookingConfirmationToCustomer: false,
      cancellationToSalon: true,
      cancellationToCustomer: true,
      rescheduleToSalon: true,
      rescheduleToCustomer: true,
      appointmentReminderToCustomer: true,
      awaitingConfirmationToSalon: true,
      cancelledBySalonToCustomer: true,
      rescheduledBySalonToCustomer: true,
      appointmentReminder2hToCustomer: true,
      staffBookedAppointmentToCustomer: true));
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingConfirmedEventHandler(
      fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance);

    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var only = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.NewBookingToSalon, only.Type);
  }

  [Fact]
  public async Task AppointmentCancelled_AllEnabled_DispatchesToSalonAndCustomer()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentCancelledEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentCancelledEventHandler>.Instance);

    await handler.Handle(new AppointmentCancelledEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    Assert.Contains(dispatcher.Messages, m => m.Type == NotificationType.CancellationToSalon);
    Assert.Contains(dispatcher.Messages, m => m.Type == NotificationType.CancellationToCustomer);
  }

  [Fact]
  public async Task AppointmentRescheduled_CarriesOldSlotInPayload()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentRescheduledEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentRescheduledEventHandler>.Instance);
    var oldDate = new DateOnly(2026, 1, 1);
    var oldStart = new TimeOnly(9, 0);

    await handler.Handle(
      new AppointmentRescheduledEvent(fx.Tenant.Id, fx.Appointment.Id, oldDate, oldStart), ct);

    var toCustomer = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.RescheduleToCustomer);
    Assert.Equal(oldDate, toCustomer.Payload.OldDate);
    Assert.Equal(oldStart, toCustomer.Payload.OldStartTime);
  }

  [Fact]
  public async Task Handler_SwallowsDispatcherExceptions()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var handler = new BookingConfirmedEventHandler(
      fx.Db, new ThrowingDispatcher(), NullLogger<BookingConfirmedEventHandler>.Instance);

    // Brak wyjątku = best-effort działa.
    await handler.Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);
  }

  [Fact]
  public async Task Handler_UnknownAppointment_DispatchesNothing()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentCancelledEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentCancelledEventHandler>.Instance);

    await handler.Handle(new AppointmentCancelledEvent(fx.Tenant.Id, Guid.NewGuid()), ct);

    Assert.Empty(dispatcher.Messages);
  }

  [Fact]
  public async Task BookingAwaitingConfirmation_DispatchesToSalon()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new BookingAwaitingConfirmationEventHandler(
      fx.Db, dispatcher, NullLogger<BookingAwaitingConfirmationEventHandler>.Instance);

    await handler.Handle(new BookingAwaitingConfirmationEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.AwaitingConfirmationToSalon, msg.Type);
    Assert.Equal(fx.Employee.Email, msg.Recipient.Email);
  }

  [Fact]
  public async Task ToSalonNotifications_AddressedToAssignedEmployeeAccount()
  {
    // Regresja/klucz dla nie-właścicieli: każde powiadomienie „do salonu" musi być zaadresowane
    // do KONTA pracownika PRZYPISANEGO do wizyty (`Recipient.UserId == employee.UserId`), dowolna
    // rola — Manager/Employee, nie tylko właściciel. Kanał in-app persistuje po `RecipientUserId`,
    // więc podstawienie tam np. ownera zamiast przypisanego pracownika = pracownik nie zobaczy
    // dzwonka. Reszta testów sprawdzała tylko `Recipient.Email` i miała `Employee.UserId == null`,
    // przez co to przypisanie było niezasertowane.
    var ct = TestContext.Current.CancellationToken;
    var employeeUserId = Guid.NewGuid();
    var fx = await SetupAsync(ct, employeeUserId: employeeUserId);
    var dispatcher = new CapturingDispatcher();

    await new BookingConfirmedEventHandler(fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance)
      .Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);
    await new AppointmentCancelledEventHandler(fx.Db, dispatcher, NullLogger<AppointmentCancelledEventHandler>.Instance)
      .Handle(new AppointmentCancelledEvent(fx.Tenant.Id, fx.Appointment.Id), ct);
    await new AppointmentRescheduledEventHandler(fx.Db, dispatcher, NullLogger<AppointmentRescheduledEventHandler>.Instance)
      .Handle(new AppointmentRescheduledEvent(fx.Tenant.Id, fx.Appointment.Id, new DateOnly(2026, 1, 1), new TimeOnly(9, 0)), ct);
    await new BookingAwaitingConfirmationEventHandler(fx.Db, dispatcher, NullLogger<BookingAwaitingConfirmationEventHandler>.Instance)
      .Handle(new BookingAwaitingConfirmationEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toSalon = dispatcher.Messages
      .Where(m => m.Type is NotificationType.NewBookingToSalon or NotificationType.CancellationToSalon
        or NotificationType.RescheduleToSalon or NotificationType.AwaitingConfirmationToSalon)
      .ToList();
    Assert.Equal(4, toSalon.Count);
    Assert.All(toSalon, m => Assert.Equal(employeeUserId, m.Recipient.UserId));
    Assert.All(toSalon, m => Assert.Equal(fx.Employee.Email, m.Recipient.Email));
  }

  [Fact]
  public async Task ToSalonNotification_EmployeeWithoutAccount_HasNullRecipientUserId()
  {
    // Dokumentuje znaną granicę: pracownik-„zasób" bez konta panelu (`UserId == null`) → wiadomość
    // do salonu wychodzi (e-mail), ale `Recipient.UserId` jest null, więc kanał in-app ją pominie
    // (patrz InAppNotificationChannelTests). Gdyby ktoś to „naprawił", ten test przypomni o decyzji.
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, employeeUserId: null);
    var dispatcher = new CapturingDispatcher();

    await new BookingConfirmedEventHandler(fx.Db, dispatcher, NullLogger<BookingConfirmedEventHandler>.Instance)
      .Handle(new BookingConfirmedEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var toSalon = Assert.Single(dispatcher.Messages, m => m.Type == NotificationType.NewBookingToSalon);
    Assert.Null(toSalon.Recipient.UserId);
  }

  [Fact]
  public async Task AppointmentConfirmedBySalon_DispatchesToCustomer()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentConfirmedBySalonEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentConfirmedBySalonEventHandler>.Instance);

    await handler.Handle(new AppointmentConfirmedBySalonEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.BookingConfirmationToCustomer, msg.Type);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon).
    Assert.Equal(fx.Customer.PhoneNumber!.Value, msg.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(msg.Recipient.Email));
  }

  [Theory]
  [InlineData(true, "odrzucona")]
  [InlineData(false, "odwołana")]
  public async Task AppointmentCancelledBySalon_WordsMessageByWasPending(bool wasPending, string expectedWord)
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentCancelledBySalonEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentCancelledBySalonEventHandler>.Instance);

    await handler.Handle(new AppointmentCancelledBySalonEvent(fx.Tenant.Id, fx.Appointment.Id, wasPending), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.CancelledBySalonToCustomer, msg.Type);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon).
    Assert.Equal(fx.Customer.PhoneNumber!.Value, msg.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(msg.Recipient.Email));
    Assert.Contains(expectedWord, msg.ShortText, StringComparison.Ordinal);
  }

  [Fact]
  public async Task AppointmentRescheduledBySalon_CarriesOldSlotInPayload()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentRescheduledBySalonEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentRescheduledBySalonEventHandler>.Instance);
    var oldDate = new DateOnly(2026, 2, 2);
    var oldStart = new TimeOnly(8, 30);

    await handler.Handle(
      new AppointmentRescheduledBySalonEvent(fx.Tenant.Id, fx.Appointment.Id, oldDate, oldStart), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.RescheduledBySalonToCustomer, msg.Type);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon).
    Assert.Equal(fx.Customer.PhoneNumber!.Value, msg.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(msg.Recipient.Email));
    Assert.Equal(oldDate, msg.Payload.OldDate);
    Assert.Equal(oldStart, msg.Payload.OldStartTime);
  }

  [Fact]
  public async Task StaffBookedAppointment_DispatchesToCustomer()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new StaffBookedAppointmentEventHandler(
      fx.Db, dispatcher, NullLogger<StaffBookedAppointmentEventHandler>.Instance);

    await handler.Handle(new StaffBookedAppointmentEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(NotificationType.StaffBookedAppointmentToCustomer, msg.Type);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon).
    Assert.Equal(fx.Customer.PhoneNumber!.Value, msg.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(msg.Recipient.Email));
  }

  [Fact]
  public async Task StaffBookedAppointment_SettingDisabled_DispatchesNothing()
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct, settings: new NotificationSettings(
      newBookingToSalon: true,
      bookingConfirmationToCustomer: true,
      cancellationToSalon: true,
      cancellationToCustomer: true,
      rescheduleToSalon: true,
      rescheduleToCustomer: true,
      appointmentReminderToCustomer: true,
      awaitingConfirmationToSalon: true,
      cancelledBySalonToCustomer: true,
      rescheduledBySalonToCustomer: true,
      appointmentReminder2hToCustomer: true,
      staffBookedAppointmentToCustomer: false));
    var dispatcher = new CapturingDispatcher();
    var handler = new StaffBookedAppointmentEventHandler(
      fx.Db, dispatcher, NullLogger<StaffBookedAppointmentEventHandler>.Instance);

    await handler.Handle(new StaffBookedAppointmentEvent(fx.Tenant.Id, fx.Appointment.Id), ct);

    Assert.Empty(dispatcher.Messages);
  }

  [Theory]
  [InlineData(true, NotificationType.AppointmentReminderToCustomer)]
  [InlineData(false, NotificationType.AppointmentReminder2hToCustomer)]
  public async Task AppointmentReminderDue_PicksTypeByWindow(bool is24h, NotificationType expectedType)
  {
    var ct = TestContext.Current.CancellationToken;
    var fx = await SetupAsync(ct);
    var dispatcher = new CapturingDispatcher();
    var handler = new AppointmentReminderDueEventHandler(
      fx.Db, dispatcher, NullLogger<AppointmentReminderDueEventHandler>.Instance);

    await handler.Handle(new AppointmentReminderDueEvent(fx.Tenant.Id, fx.Appointment.Id, is24h), ct);

    var msg = Assert.Single(dispatcher.Messages);
    Assert.Equal(expectedType, msg.Type);
    // Domyślny kanał klienta = Phone → adresat dostaje SMS (telefon).
    Assert.Equal(fx.Customer.PhoneNumber!.Value, msg.Recipient.Phone);
    Assert.True(string.IsNullOrEmpty(msg.Recipient.Email));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private sealed record Fixture(
    ApplicationDbContext Db,
    Tenant Tenant,
    Customer Customer,
    Employee Employee,
    Service Service,
    Appointment Appointment);

  private static async Task<Fixture> SetupAsync(
    CancellationToken ct,
    NotificationSettings? settings = null,
    CustomerVerificationChannel verificationChannel = CustomerVerificationChannel.Phone,
    string customerEmail = "customer@notif.local",
    Guid? employeeUserId = null)
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));

    var tenant = new Tenant("Notif Salon", "notif-salon-" + Guid.NewGuid().ToString("N")[..6]);
    typeof(Entity).GetProperty("Id")!.SetValue(tenant, tenantId);
    // Fixture domyślnie z wszystkimi powiadomieniami włączonymi — testy sprawdzają logikę
    // rozsyłki, nie produkcyjny default salonu (NotificationSettings.Defaults()).
    tenant.Update(tenant.Name, tenant.Slug, verificationChannel,
      notificationSettings: settings ?? NotificationSettings.AllEnabled());

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, employeeUserId, "Ann", "Smith", "ann@notif.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Cut", new Money(80m, "PLN"), 30);
    // Pusty e-mail (klient „tylko na SMS-ie") tworzymy ścieżką rezerwacji publicznej —
    // zwykły konstruktor wymaga poprawnego adresu e-mail.
    var customer = string.IsNullOrEmpty(customerEmail)
      ? Customer.CreateFromPublicBooking(tenant.Id, email: null, new PhoneNumber("+48501234567"))
      : new Customer(tenant.Id, "Jan", "Kowalski", customerEmail, new PhoneNumber("+48501234567"), "");
    var appointment = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), new TimeOnly(10, 0), new TimeOnly(10, 30),
      AppointmentStatus.Booked, new Money(80m, "PLN"), string.Empty, null);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Customers.Add(customer);
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync(ct);

    return new Fixture(db, tenant, customer, employee, service, appointment);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

  private sealed class CapturingDispatcher : INotificationDispatcher
  {
    public List<NotificationMessage> Messages { get; } = new();
    public Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct)
    {
      Messages.Add(message);
      return Task.FromResult(new NotificationDispatchResult(
      [
        new(NotificationChannelKind.Sms, NotificationDeliveryStatus.Sent),
        new(NotificationChannelKind.Email, NotificationDeliveryStatus.Sent),
      ]));
    }
  }

  private sealed class ThrowingDispatcher : INotificationDispatcher
  {
    public Task<NotificationDispatchResult> DispatchAsync(NotificationMessage message, CancellationToken ct)
      => throw new InvalidOperationException("boom");
  }
}
