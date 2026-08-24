using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Application.UnitTests.Booking;

/// <summary>
/// AttachInspirationImageCommandHandler — deferred-upload (#2). Token grantu (wydany po OTP) autoryzuje
/// upload zdjęcia do KONKRETNEJ wizyty. Sprawdzamy: happy path (obraz przetworzony + podpięty), 403 dla
/// zepsutego tokenu, 403 dla tokenu wydanego dla innej wizyty, twardy cap (3) PRZED uploadem (zero sierot),
/// oraz izolację tenanta (cudza wizyta = 404, brak uploadu).
/// </summary>
public sealed class AttachInspirationImageHandlerTests
{
  private const string Url = "https://cdn.test/inspirations/abc.webp";
  private const string Thumb = "https://cdn.test/inspirations/abc_thumb.webp";
  private const string Key = "inspirations/abc.webp";
  private const string ThumbKey = "inspirations/abc_thumb.webp";

  private static Appointment NewAppointment(Guid tenantId)
      => new(
          tenantId, Guid.NewGuid(), Guid.NewGuid(), null,
          new DateOnly(2026, 7, 1), new TimeOnly(10, 0), new TimeOnly(11, 0),
          AppointmentStatus.Booked, new Money(100m, "PLN"), "", null);

  private sealed class FakeRepo : IAppointmentRepository
  {
    private readonly Appointment? _appointment;
    public FakeRepo(Appointment? appointment) => _appointment = appointment;
    public Task<Appointment?> GetByIdAsync(Guid id)
        => Task.FromResult(_appointment is not null && _appointment.Id == id ? _appointment : null);

    public Task AddAsync(Appointment appointment) => Task.CompletedTask;
    public void Update(Appointment appointment) { }
    public void Remove(Appointment appointment) { }
    public Task<bool> HasCollisionAsync(Guid e, TimeRange t, DateOnly d, Guid ten, Guid? ig = null) => Task.FromResult(false);
    public Task<bool> HasCollisionAsync(Guid e, TimeOnly s, TimeOnly en, DateOnly d, Guid ten, Guid? ig = null) => Task.FromResult(false);
    public Task<bool> HasCollisionAsync(Guid e, TimeRange t, DateOnly d, Guid ten, IReadOnlyCollection<Guid> ig) => Task.FromResult(false);
    public Task<bool> HasCollisionInDateRangeAsync(Guid e, DateOnly s, DateOnly en, Guid ten) => Task.FromResult(false);
    public Task<List<Appointment>> GetAppointmentsByDateAsync(Guid e, DateOnly d, Guid ten) => Task.FromResult(new List<Appointment>());
    public Task<List<Appointment>> GetAppointmentsInDateRangeAsync(Guid e, DateOnly s, DateOnly en, Guid ten) => Task.FromResult(new List<Appointment>());
  }

  private sealed class FakeTenantRepo : ITenantRepository
  {
    private readonly Tenant _tenant;
    public FakeTenantRepo(Tenant tenant) => _tenant = tenant;
    public Task<Tenant?> GetByIdAsync(Guid id) => Task.FromResult<Tenant?>(_tenant);
    public Task AddAsync(Tenant tenant) => Task.CompletedTask;
    public void Update(Tenant tenant) { }
    public void Remove(Tenant tenant) { }
  }

  private static Tenant TenantWith(bool collectInspirations)
  {
    var t = new Tenant("Salon", "salon");
    // Domyślnie funkcja jest wyłączona — ustawiamy jawnie pod scenariusz testu.
    t.Update("Salon", "salon", collectInspirationImages: collectInspirations);
    return t;
  }

  private sealed class FakeImageProcessing : IImageProcessingService
  {
    public int Calls { get; private set; }
    public Task<ProcessedImageResult> ProcessAndStoreAsync(Stream content, string keyPrefix, CancellationToken ct = default)
    {
      Calls++;
      return Task.FromResult(new ProcessedImageResult(Key, Url, Thumb, ThumbKey));
    }
  }

  private sealed class FakeUow : IUnitOfWork
  {
    public int Saves { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => action(ct);
  }

  private sealed class FakeTenant : ICurrentTenantService { public Guid? TenantId { get; set; } }

  /// <summary>Token = appointmentId zakodowany jako "N"; trywialny, ale rozróżnia wizyty (anti-cross-appointment).</summary>
  private sealed class FakeTokens : IInspirationUploadTokenService
  {
    public string Issue(Guid appointmentId) => appointmentId.ToString("N");
    public bool TryValidate(string token, out Guid appointmentId) => Guid.TryParseExact(token, "N", out appointmentId);
  }

  private static AttachInspirationImageCommandHandler Handler(
      Appointment? appt, Guid? tenantId, FakeImageProcessing img, FakeUow uow, bool collectInspirations = true)
      => new(
          new FakeRepo(appt),
          new FakeTenantRepo(TenantWith(collectInspirations)),
          new FakeTokens(),
          img,
          uow,
          new FakeTenant { TenantId = tenantId });

  private static Stream Png() => new MemoryStream(new byte[] { 1, 2, 3 });

  [Fact]
  public async Task ValidToken_processes_and_attaches_image()
  {
    var tenantId = Guid.NewGuid();
    var appt = NewAppointment(tenantId);
    var img = new FakeImageProcessing();
    var uow = new FakeUow();
    var handler = Handler(appt, tenantId, img, uow);

    var result = await handler.Handle(
        new AttachInspirationImageCommand(appt.Id, appt.Id.ToString("N"), Png()), CancellationToken.None);

    Assert.Equal(Url, result.Url);
    Assert.Equal(Key, result.Key);
    Assert.Single(appt.InspirationImages);
    // Klucz miniatury jest persystowany — niezbędny do późniejszego skasowania z R2.
    Assert.Equal(ThumbKey, appt.InspirationImages.Single().ThumbnailStorageKey);
    Assert.Equal(1, img.Calls);
    Assert.Equal(1, uow.Saves);
  }

  [Fact]
  public async Task Invalid_token_is_forbidden_and_does_not_upload()
  {
    var tenantId = Guid.NewGuid();
    var appt = NewAppointment(tenantId);
    var img = new FakeImageProcessing();
    var handler = Handler(appt, tenantId, img, new FakeUow());

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        handler.Handle(new AttachInspirationImageCommand(appt.Id, "not-a-token", Png()), CancellationToken.None));

    Assert.Equal(ErrorCodes.AppointmentInspirationUploadForbidden, ex.ErrorCode);
    Assert.Equal(0, img.Calls);
    Assert.Empty(appt.InspirationImages);
  }

  [Fact]
  public async Task Token_for_other_appointment_is_forbidden()
  {
    var tenantId = Guid.NewGuid();
    var appt = NewAppointment(tenantId);
    var img = new FakeImageProcessing();
    var handler = Handler(appt, tenantId, img, new FakeUow());

    // Token wydany dla innej wizyty (inny Guid) — nie pasuje do route appointmentId.
    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        handler.Handle(
            new AttachInspirationImageCommand(appt.Id, Guid.NewGuid().ToString("N"), Png()),
            CancellationToken.None));

    Assert.Equal(ErrorCodes.AppointmentInspirationUploadForbidden, ex.ErrorCode);
    Assert.Equal(0, img.Calls);
  }

  [Fact]
  public async Task Cap_is_enforced_before_upload_no_orphan()
  {
    var tenantId = Guid.NewGuid();
    var appt = NewAppointment(tenantId);
    appt.SetInspirationImages(new[]
    {
      new AppointmentInspirationLine(Url, Thumb, "inspirations/1.webp", "inspirations/1_thumb.webp"),
      new AppointmentInspirationLine(Url, Thumb, "inspirations/2.webp", "inspirations/2_thumb.webp"),
      new AppointmentInspirationLine(Url, Thumb, "inspirations/3.webp", "inspirations/3_thumb.webp"),
    });
    var img = new FakeImageProcessing();
    var handler = Handler(appt, tenantId, img, new FakeUow());

    var ex = await Assert.ThrowsAsync<AppointmentBookingRuleException>(() =>
        handler.Handle(new AttachInspirationImageCommand(appt.Id, appt.Id.ToString("N"), Png()), CancellationToken.None));

    Assert.Equal(ErrorCodes.AppointmentTooManyInspirationImages, ex.ErrorCode);
    // Kluczowe dla braku sierot: obraz NIE został przetworzony/wgrany.
    Assert.Equal(0, img.Calls);
    Assert.Equal(3, appt.InspirationImages.Count);
  }

  [Fact]
  public async Task Disabled_feature_is_forbidden_and_does_not_upload()
  {
    var tenantId = Guid.NewGuid();
    var appt = NewAppointment(tenantId);
    var img = new FakeImageProcessing();
    // Salon wyłączył zbieranie inspiracji → serwer odrzuca upload mimo ważnego tokenu.
    var handler = Handler(appt, tenantId, img, new FakeUow(), collectInspirations: false);

    var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
        handler.Handle(new AttachInspirationImageCommand(appt.Id, appt.Id.ToString("N"), Png()), CancellationToken.None));

    Assert.Equal(ErrorCodes.AppointmentInspirationUploadForbidden, ex.ErrorCode);
    Assert.Equal(0, img.Calls);
    Assert.Empty(appt.InspirationImages);
  }

  [Fact]
  public async Task Cross_tenant_appointment_is_not_found_and_does_not_upload()
  {
    var apptTenant = Guid.NewGuid();
    var appt = NewAppointment(apptTenant);
    var img = new FakeImageProcessing();
    // Bieżący tenant ze slugu różny od tenanta wizyty.
    var handler = Handler(appt, Guid.NewGuid(), img, new FakeUow());

    await Assert.ThrowsAsync<NotFoundException>(() =>
        handler.Handle(new AttachInspirationImageCommand(appt.Id, appt.Id.ToString("N"), Png()), CancellationToken.None));

    Assert.Equal(0, img.Calls);
  }
}
