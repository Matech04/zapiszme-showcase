using App.Application.Common.Interfaces;
using App.Application.Guides;
using App.Application.Guides.Commands.MarkGuideCompleted;
using App.Application.Guides.Commands.ResetGuideCompletion;
using App.Application.Guides.Queries.GetGuideCompletions;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Guides;

/// <summary>
/// APP-GUIDE — postęp przewodników. Kluczowe własności: idempotencja zapisu, izolacja między
/// użytkownikami (encja NIE jest tenant-scoped, więc filtrem jest wyłącznie UserId) oraz sufit
/// wierszy chroniący przed zapełnieniem tabeli.
/// </summary>
public sealed class GuideCompletionHandlerTests
{
  private const string GuideId = "set-weekly-schedule";

  // GUIDE-001: pierwsze ukończenie zapisuje wiersz
  [Fact]
  public async Task Mark_completed_persists_guide_for_user()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var userId = Guid.NewGuid();

    await new MarkGuideCompletedCommandHandler(db)
      .Handle(new MarkGuideCompletedCommand(userId, GuideId), ct);

    var completions = await new GetGuideCompletionsQueryHandler(db)
      .Handle(new GetGuideCompletionsQuery(userId), ct);

    Assert.Equal(new[] { GuideId }, completions);
  }

  // GUIDE-002: powtórne wywołanie nie tworzy duplikatu ani nie przesuwa daty pierwszego ukończenia
  [Fact]
  public async Task Mark_completed_is_idempotent_and_keeps_original_timestamp()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var userId = Guid.NewGuid();
    var handler = new MarkGuideCompletedCommandHandler(db);

    await handler.Handle(new MarkGuideCompletedCommand(userId, GuideId), ct);
    var firstCompletedAt = await db.UserGuideCompletions
      .Where(c => c.UserId == userId)
      .Select(c => c.CompletedAtUtc)
      .SingleAsync(ct);

    await handler.Handle(new MarkGuideCompletedCommand(userId, GuideId), ct);

    var rows = await db.UserGuideCompletions.Where(c => c.UserId == userId).ToListAsync(ct);
    Assert.Single(rows);
    Assert.Equal(firstCompletedAt, rows[0].CompletedAtUtc);
  }

  // GUIDE-003: postęp jednego użytkownika jest niewidoczny dla drugiego.
  // To odpowiednik testu TenantViolation dla encji kluczowanej userem, nie salonem —
  // jedyną barierą jest filtr po UserId, więc musi mieć własne pokrycie.
  [Fact]
  public async Task Completions_are_isolated_between_users()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var mine = Guid.NewGuid();
    var someoneElse = Guid.NewGuid();

    await new MarkGuideCompletedCommandHandler(db)
      .Handle(new MarkGuideCompletedCommand(someoneElse, GuideId), ct);

    var completions = await new GetGuideCompletionsQueryHandler(db)
      .Handle(new GetGuideCompletionsQuery(mine), ct);

    Assert.Empty(completions);
  }

  // GUIDE-004: reset zdejmuje znacznik i jest idempotentny przy braku wiersza
  [Fact]
  public async Task Reset_removes_completion_and_tolerates_missing_row()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var userId = Guid.NewGuid();
    var reset = new ResetGuideCompletionCommandHandler(db);

    await new MarkGuideCompletedCommandHandler(db)
      .Handle(new MarkGuideCompletedCommand(userId, GuideId), ct);

    await reset.Handle(new ResetGuideCompletionCommand(userId, GuideId), ct);
    Assert.Empty(await db.UserGuideCompletions.Where(c => c.UserId == userId).ToListAsync(ct));

    // Drugi reset nie może rzucić — katalog woła go bez sprawdzania stanu.
    await reset.Handle(new ResetGuideCompletionCommand(userId, GuideId), ct);
  }

  // GUIDE-005: reset nie rusza cudzego postępu o tym samym GuideId
  [Fact]
  public async Task Reset_does_not_touch_another_users_completion()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var mine = Guid.NewGuid();
    var someoneElse = Guid.NewGuid();
    var mark = new MarkGuideCompletedCommandHandler(db);

    await mark.Handle(new MarkGuideCompletedCommand(mine, GuideId), ct);
    await mark.Handle(new MarkGuideCompletedCommand(someoneElse, GuideId), ct);

    await new ResetGuideCompletionCommandHandler(db)
      .Handle(new ResetGuideCompletionCommand(mine, GuideId), ct);

    Assert.Single(await db.UserGuideCompletions.Where(c => c.UserId == someoneElse).ToListAsync(ct));
  }

  // GUIDE-006: sufit wierszy na użytkownika — endpoint przyjmuje dowolny GuideId, więc bez tego
  // zalogowany klient mógłby w pętli zapełnić tabelę
  [Fact]
  public async Task Mark_completed_stops_at_per_user_cap()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupDb();
    var userId = Guid.NewGuid();

    for (var i = 0; i < GuideIdRules.MaxPerUser; i++)
    {
      db.UserGuideCompletions.Add(
        UserGuideCompletion.Create(userId, $"filler-{i}", DateTime.UtcNow));
    }

    await db.SaveChangesAsync(ct);

    await new MarkGuideCompletedCommandHandler(db)
      .Handle(new MarkGuideCompletedCommand(userId, "one-too-many"), ct);

    var count = await db.UserGuideCompletions.CountAsync(c => c.UserId == userId, ct);
    Assert.Equal(GuideIdRules.MaxPerUser, count);
  }

  // GUIDE-007: walidator odrzuca identyfikatory spoza kebab-case (ochrona przed śmieciowym wejściem)
  [Theory]
  [InlineData("")]
  [InlineData("Set-Weekly-Schedule")]
  [InlineData("set_weekly_schedule")]
  [InlineData("set weekly schedule")]
  [InlineData("../../etc/passwd")]
  public void Validator_rejects_malformed_guide_ids(string guideId)
  {
    var result = new MarkGuideCompletedCommandValidator()
      .Validate(new MarkGuideCompletedCommand(Guid.NewGuid(), guideId));

    Assert.False(result.IsValid);
  }

  // GUIDE-007: poprawny kebab-case przechodzi
  [Fact]
  public void Validator_accepts_kebab_case_guide_id()
  {
    var result = new MarkGuideCompletedCommandValidator()
      .Validate(new MarkGuideCompletedCommand(Guid.NewGuid(), GuideId));

    Assert.True(result.IsValid);
  }

  // Encja nie jest tenant-scoped, ale ApplicationDbContext wymaga ICurrentTenantService.
  private static ApplicationDbContext SetupDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService(Guid.NewGuid()));
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
