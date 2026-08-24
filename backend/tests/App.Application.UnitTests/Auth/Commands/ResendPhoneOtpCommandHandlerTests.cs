using App.Application.Auth.Commands.ResendPhoneOtp;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using App.Application.UnitTests.TestSupport;

namespace App.Application.UnitTests.Auth.Commands;

/// <summary>AUTH-RESENDOTP-* — gate EmailConfirmed + cooldown + delegacja do SendPhoneOtp.</summary>
public sealed class ResendPhoneOtpCommandHandlerTests
{
  [Fact]
  public async Task Handle_EmailNotConfirmed_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = new User("e@e.co", "U") { EmailConfirmed = false, PhoneNumber = "+48500000000" };
    var db = await NewDbWith(user);
    var mediator = new RecordingMediator();

    await Assert.ThrowsAsync<PhoneOtpEmailNotConfirmedException>(
      () => NewHandler(db, mediator).Handle(new ResendPhoneOtpCommand(user.Id), ct));
    Assert.Empty(mediator.Sent);
  }

  [Fact]
  public async Task Handle_PhoneAlreadyConfirmed_Throws()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = new User("e@e.co", "U")
    {
      EmailConfirmed = true,
      PhoneNumber = "+48500000000",
      PhoneNumberConfirmed = true,
    };
    var db = await NewDbWith(user);
    var mediator = new RecordingMediator();

    await Assert.ThrowsAsync<PhoneAlreadyConfirmedException>(
      () => NewHandler(db, mediator).Handle(new ResendPhoneOtpCommand(user.Id), ct));
    Assert.Empty(mediator.Sent);
  }

  [Fact]
  public async Task Handle_TooSoonAfterPreviousOtp_ThrowsCooldown()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = NewActiveUser();
    var clock = new TestTimeProvider();
    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var recentOtp = PhoneConfirmationOtp.Create(user.Id, "h", nowUtc, TimeSpan.FromMinutes(10));
    var db = await NewDbWith(user, recentOtp);
    var mediator = new RecordingMediator();

    clock.Advance(TimeSpan.FromSeconds(30)); // mniej niż 60s cooldown

    var ex = await Assert.ThrowsAsync<PhoneOtpCooldownException>(
      () => NewHandler(db, mediator, clock).Handle(new ResendPhoneOtpCommand(user.Id), ct));
    Assert.InRange(ex.RetryAfterSeconds, 1, 60);
    Assert.Empty(mediator.Sent);
  }

  [Fact]
  public async Task Handle_AfterCooldown_DelegatesToSendCommand()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = NewActiveUser();
    var clock = new TestTimeProvider();
    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var oldOtp = PhoneConfirmationOtp.Create(user.Id, "h", nowUtc, TimeSpan.FromMinutes(10));
    var db = await NewDbWith(user, oldOtp);
    var mediator = new RecordingMediator();

    clock.Advance(TimeSpan.FromSeconds(61));

    await NewHandler(db, mediator, clock).Handle(new ResendPhoneOtpCommand(user.Id), ct);
    var sent = Assert.Single(mediator.Sent);
    Assert.IsType<App.Application.Auth.Commands.SendPhoneOtp.SendPhoneOtpCommand>(sent);
  }

  [Fact]
  public async Task Handle_NoPriorOtp_DelegatesImmediately()
  {
    var ct = TestContext.Current.CancellationToken;
    var user = NewActiveUser();
    var db = await NewDbWith(user);
    var mediator = new RecordingMediator();

    await NewHandler(db, mediator).Handle(new ResendPhoneOtpCommand(user.Id), ct);
    Assert.Single(mediator.Sent);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────

  private static User NewActiveUser() =>
    new("owner@e.co", "Jan")
    {
      EmailConfirmed = true,
      PhoneNumber = "+48501234567",
      PhoneNumberConfirmed = false,
    };

  private static ResendPhoneOtpCommandHandler NewHandler(
    ApplicationDbContext db, IMediator mediator, TestTimeProvider? clock = null)
  {
    var config = new ConfigurationBuilder().AddInMemoryCollection(
      new Dictionary<string, string?>
      {
        ["Auth:PhoneOtpResendCooldownSeconds"] = "60",
      }).Build();
    return new ResendPhoneOtpCommandHandler(db, mediator, clock ?? new TestTimeProvider(), config);
  }

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

  private sealed class RecordingMediator : IMediator
  {
    public List<object> Sent { get; } = new();

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
      where TRequest : notnull, IRequest
    {
      Sent.Add(request);
      return Task.CompletedTask;
    }

    // Brak użycia w teście — minimalne stuby.
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
      throw new NotImplementedException();
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
      throw new NotImplementedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
      throw new NotImplementedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
      throw new NotImplementedException();
    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
      throw new NotImplementedException();
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
      where TNotification : INotification =>
      throw new NotImplementedException();
  }
}
