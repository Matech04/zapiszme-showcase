using App.Application.Auth.Commands.ConfirmPhone;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using App.Application.UnitTests.TestSupport;

namespace App.Application.UnitTests.Auth.Commands;

/// <summary>AUTH-CONFIRMPHONE-* — weryfikacja kodu SMS.</summary>
public sealed class ConfirmPhoneCommandHandlerTests
{
  [Fact]
  public async Task Handle_HappyPath_SetsPhoneNumberConfirmedAndConsumesOtp()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateConfirmedEmailUser();
    var clock = new TestTimeProvider();
    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var code = "123456";
    var otp = PhoneConfirmationOtp.Create(user.Id, OtpCodeHasher.Hash(code), nowUtc, TimeSpan.FromMinutes(10));
    var db = await NewDbWith(user, otp);

    await NewHandler(db, clock).Handle(new ConfirmPhoneCommand(user.Id, code), ct);

    db.ChangeTracker.Clear();
    var reloadedUser = await db.Users.SingleAsync(u => u.Id == user.Id, ct);
    Assert.True(reloadedUser.PhoneNumberConfirmed);
    var reloadedOtp = await db.PhoneConfirmationOtps.SingleAsync(o => o.Id == otp.Id, ct);
    Assert.NotNull(reloadedOtp.ConsumedAt);
  }

  [Fact]
  public async Task Handle_EmailNotConfirmed_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = new User("e@e.co", "U") { EmailConfirmed = false, PhoneNumber = "+48500000000" };
    var db = await NewDbWith(user);

    await Assert.ThrowsAsync<PhoneOtpEmailNotConfirmedException>(
      () => NewHandler(db).Handle(new ConfirmPhoneCommand(user.Id, "123456"), ct));
  }

  [Fact]
  public async Task Handle_NoActiveOtp_ThrowsExpired()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateConfirmedEmailUser();
    var db = await NewDbWith(user);

    await Assert.ThrowsAsync<PhoneOtpExpiredException>(
      () => NewHandler(db).Handle(new ConfirmPhoneCommand(user.Id, "123456"), ct));
  }

  [Fact]
  public async Task Handle_ExpiredOtp_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateConfirmedEmailUser();
    var clock = new TestTimeProvider();
    var startUtc = clock.GetUtcNow().UtcDateTime;
    var otp = PhoneConfirmationOtp.Create(user.Id, OtpCodeHasher.Hash("123456"), startUtc, TimeSpan.FromMinutes(10));
    var db = await NewDbWith(user, otp);

    clock.Advance(TimeSpan.FromMinutes(11));

    await Assert.ThrowsAsync<PhoneOtpExpiredException>(
      () => NewHandler(db, clock).Handle(new ConfirmPhoneCommand(user.Id, "123456"), ct));
  }

  [Fact]
  public async Task Handle_WrongCode_IncrementsAttempts()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateConfirmedEmailUser();
    var clock = new TestTimeProvider();
    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var otp = PhoneConfirmationOtp.Create(user.Id, OtpCodeHasher.Hash("123456"), nowUtc, TimeSpan.FromMinutes(10));
    var db = await NewDbWith(user, otp);

    var ex = await Assert.ThrowsAsync<PhoneOtpInvalidException>(
      () => NewHandler(db, clock).Handle(new ConfirmPhoneCommand(user.Id, "999999"), ct));
    Assert.Equal(4, ex.RemainingAttempts);

    db.ChangeTracker.Clear();
    var reloaded = await db.PhoneConfirmationOtps.SingleAsync(ct);
    Assert.Equal(1, reloaded.AttemptsCount);
    Assert.Null(reloaded.ConsumedAt);
  }

  [Fact]
  public async Task Handle_FifthFailedAttempt_LocksOtp()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = CreateConfirmedEmailUser();
    var clock = new TestTimeProvider();
    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var otp = PhoneConfirmationOtp.Create(user.Id, OtpCodeHasher.Hash("123456"), nowUtc, TimeSpan.FromMinutes(10));
    for (var i = 0; i < 4; i++)
    {
      otp.RegisterFailedAttempt();
    }
    var db = await NewDbWith(user, otp);

    // 5. próba — zła → AttemptsCount=5 → kolejne wywołania zwracają Locked.
    var firstEx = await Assert.ThrowsAsync<PhoneOtpInvalidException>(
      () => NewHandler(db, clock).Handle(new ConfirmPhoneCommand(user.Id, "999999"), ct));
    Assert.Equal(0, firstEx.RemainingAttempts);

    await Assert.ThrowsAsync<PhoneOtpLockedException>(
      () => NewHandler(db, clock).Handle(new ConfirmPhoneCommand(user.Id, "123456"), ct));
  }

  [Fact]
  public async Task Handle_AlreadyConfirmed_NoOp()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = new User("e@e.co", "U")
    {
      EmailConfirmed = true,
      PhoneNumber = "+48500000000",
      PhoneNumberConfirmed = true,
    };
    var db = await NewDbWith(user);

    await NewHandler(db).Handle(new ConfirmPhoneCommand(user.Id, "999999"), ct);
    // Nie rzuca — idempotentny no-op.
  }

  // ── helpers ────────────────────────────────────────────────────────────────────

  private static User CreateConfirmedEmailUser() =>
    new("owner@e.co", "Jan") { EmailConfirmed = true, PhoneNumber = "+48501234567" };

  private static ConfirmPhoneCommandHandler NewHandler(ApplicationDbContext db, TestTimeProvider? clock = null) =>
    new(db, clock ?? new TestTimeProvider(), NullLogger<ConfirmPhoneCommandHandler>.Instance);

  private static async Task<ApplicationDbContext> NewDbWith(User user, PhoneConfirmationOtp? otp = null)
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService());
    db.Users.Add(user);
    if (otp is not null)
    {
      db.PhoneConfirmationOtps.Add(otp);
    }
    await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    db.ChangeTracker.Clear();
    return db;
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
