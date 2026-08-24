using App.Application.Auth.Commands.SendPhoneOtp;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using App.Application.UnitTests.TestSupport;

namespace App.Application.UnitTests.Auth.Commands;

/// <summary>AUTH-SENDOTP-* — handler generuje kod, invaliduje poprzednie OTP, deleguje wysyłkę.</summary>
public sealed class SendPhoneOtpCommandHandlerTests
{
  [Fact]
  public async Task Handle_HappyPath_PersistsOtpAndCallsSender()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateUser();
    var db = await NewDbWith(user);
    var sender = new RecordingSender();

    var handler = NewHandler(db, sender);
    await handler.Handle(new SendPhoneOtpCommand(user.Id), ct);

    var otp = await db.PhoneConfirmationOtps.SingleAsync(ct);
    Assert.Equal(user.Id, otp.UserId);
    Assert.Null(otp.ConsumedAt);
    Assert.False(string.IsNullOrEmpty(otp.CodeHash));
    Assert.Equal(user.PhoneNumber, sender.Calls.Single().Phone);
    Assert.Matches(@"^\d{6}$", sender.Calls.Single().Code);
  }

  [Fact]
  public async Task Handle_InvalidatesPreviousActiveOtp()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateUser();
    var db = await NewDbWith(user);
    var nowUtc = DateTime.UtcNow;
    var oldOtp = PhoneConfirmationOtp.Create(user.Id, "oldhash", nowUtc, TimeSpan.FromMinutes(10));
    db.PhoneConfirmationOtps.Add(oldOtp);
    await db.SaveChangesAsync(ct);

    var handler = NewHandler(db, new RecordingSender());
    await handler.Handle(new SendPhoneOtpCommand(user.Id), ct);

    db.ChangeTracker.Clear();
    var reloadedOld = await db.PhoneConfirmationOtps.SingleAsync(o => o.Id == oldOtp.Id, ct);
    Assert.NotNull(reloadedOld.ConsumedAt);

    var newOtps = await db.PhoneConfirmationOtps.Where(o => o.ConsumedAt == null).ToListAsync(ct);
    Assert.Single(newOtps);
  }

  [Fact]
  public async Task Handle_UserNotFound_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = NewDb();
    var handler = NewHandler(db, new RecordingSender());

    await Assert.ThrowsAsync<NotFoundException>(
      () => handler.Handle(new SendPhoneOtpCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Handle_UserHasNoPhone_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = new User("noopo@e.co", "Bez Numeru") { PhoneNumber = null };
    var db = await NewDbWith(user);

    var handler = NewHandler(db, new RecordingSender());
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => handler.Handle(new SendPhoneOtpCommand(user.Id), ct));
  }

  [Fact]
  public async Task Handle_SenderThrows_PropagatesAfterPersist()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateUser();
    var db = await NewDbWith(user);
    var sender = new RecordingSender { ThrowOnSend = true };

    var handler = NewHandler(db, sender);
    await Assert.ThrowsAsync<SmsServiceUnavailableException>(
      () => handler.Handle(new SendPhoneOtpCommand(user.Id), ct));

    // Otp został zapisany przed próbą wysyłki — user może wywołać resend.
    var otp = await db.PhoneConfirmationOtps.SingleAsync(ct);
    Assert.Null(otp.ConsumedAt);
  }

  // ── dev-owy stały kod OTP (PhoneOtpDevOptions) ─────────────────────────────────

  // Ułatwienie do ręcznego testowania onboardingu: w Development kod jest stały ("000000"),
  // więc nie trzeba go wyłuskiwać z logów przy każdym przebiegu kreatora.
  [Fact]
  public async Task Handle_WithDevFixedCode_UsesItInsteadOfRandom()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateUser();
    var db = await NewDbWith(user);
    var sender = new RecordingSender();

    var handler = NewHandler(db, sender, new PhoneOtpDevOptions("000000"));
    await handler.Handle(new SendPhoneOtpCommand(user.Id), ct);

    Assert.Equal("000000", sender.Calls.Single().Code);

    // Kluczowe: kod jest hashowany NORMALNIE, więc ConfirmPhoneCommand porówna go tak samo jak
    // w prod. Gdyby stały kod omijał hashowanie, ścieżka weryfikacji przestałaby być testowana.
    var otp = await db.PhoneConfirmationOtps.SingleAsync(ct);
    Assert.Equal(OtpCodeHasher.Hash("000000"), otp.CodeHash);
  }

  // Brak opcji (= produkcja, bo Program.cs wstrzykuje Disabled poza Development) → kod losowy.
  [Fact]
  public async Task Handle_WithoutDevOptions_KeepsRandomCode()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateUser();
    var db = await NewDbWith(user);
    var sender = new RecordingSender();

    var handler = NewHandler(db, sender, PhoneOtpDevOptions.Disabled);
    await handler.Handle(new SendPhoneOtpCommand(user.Id), ct);

    var code = sender.Calls.Single().Code;
    Assert.Matches(@"^\d{6}$", code);
    Assert.NotEqual("000000", code);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────

  private static User CreateUser() =>
    new("owner@e.co", "Jan Kowalski") { PhoneNumber = "+48501234567" };

  private static SendPhoneOtpCommandHandler NewHandler(
    ApplicationDbContext db,
    IPhoneOtpSender sender,
    PhoneOtpDevOptions? devOptions)
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(
      new Dictionary<string, string?>
      {
        ["Auth:PhoneOtpTtlMinutes"] = "10",
      }).Build();
    return new SendPhoneOtpCommandHandler(
      db,
      sender,
      new NoOpOtpProtection(),
      new TestTimeProvider(),
      config,
      NullLogger<SendPhoneOtpCommandHandler>.Instance,
      httpContextAccessor: null,
      devOptions: devOptions);
  }

  private static SendPhoneOtpCommandHandler NewHandler(ApplicationDbContext db, IPhoneOtpSender sender)
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(
      new Dictionary<string, string?>
      {
        ["Auth:PhoneOtpTtlMinutes"] = "10",
      }).Build();
    return new SendPhoneOtpCommandHandler(
      db,
      sender,
      new NoOpOtpProtection(),
      new TestTimeProvider(),
      config,
      NullLogger<SendPhoneOtpCommandHandler>.Instance);
  }

  private static ApplicationDbContext NewDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService());
  }

  private static async Task<ApplicationDbContext> NewDbWith(User user)
  {
    var db = NewDb();
    db.Users.Add(user);
    await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    db.ChangeTracker.Clear();
    return db;
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }

  private sealed class NoOpOtpProtection : IBookingOtpProtection
  {
    public void AssertCanRequestOtp(Guid appointmentId, string? clientIp) { }
    public void RegisterOtpRequestSucceeded(Guid appointmentId, string? clientIp) { }
    public void AssertCanSendOtpToEmail(string email) { }
    public void RegisterOtpSentToEmail(string email) { }
    public void AssertCanSendOtpToPhone(string phoneE164) { }
    public void RegisterOtpSentToPhone(string phoneE164) { }
    public void AssertCanSendOtpSmsFromIp(string? clientIp) { }
    public void RegisterOtpSmsSentFromIp(string? clientIp) { }

    public void AssertCanSendOtpEmailFromIp(string? clientIp) { }

    public void RegisterOtpEmailSentFromIp(string? clientIp) { }

    public void AssertCanConfirmBookingFromIp(string? clientIp) { }
    public void RegisterHoldCreatedForIp(string? clientIp) { }
    public void ReleaseHoldForIp(string? clientIp) { }
    public void RecordVerifyOtpAttempt(string? clientIp) { }
    public bool IsVerificationBlocked(Guid appointmentId) => false;
    public int RegisterFailedVerificationAttempt(Guid appointmentId) => 1;
    public void ClearVerificationAttempts(Guid appointmentId) { }
    public bool IsTargetVerificationBlocked(string target) => false;
    public int RegisterFailedVerificationForTarget(string target) => 0;
    public void ClearTargetVerificationAttempts(string target) { }
    public void AssertCanConfirmWithSession(Guid sessionToken, string? clientIp) { }
    public void AssertCanReschedule(Guid sessionToken, Guid appointmentId, string? clientIp) { }
  }

  private sealed class RecordingSender : IPhoneOtpSender
  {
    public List<(string Phone, string Code)> Calls { get; } = new();
    public bool ThrowOnSend { get; set; }

    public Task SendOtpAsync(string phoneE164, string code, CancellationToken ct)
    {
      Calls.Add((phoneE164, code));
      if (ThrowOnSend)
      {
        throw new SmsServiceUnavailableException("fake");
      }
      return Task.CompletedTask;
    }
  }
}
