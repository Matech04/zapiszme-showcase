using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.BackgroundJobs;

/// <summary>
/// Okresowo usuwa surowe dane kontaktowe (e-mail/telefon) trzymane w rekordach OTP po oknie
/// retencji — RODO/minimalizacja danych (kod OTP traci wartość po wygaśnięciu, a PII obok niego
/// nie powinno leżeć bezterminowo). Obejmuje trzy miejsca:
/// <list type="bullet">
///   <item><c>SelfServiceOtps</c> — encja tenantowa (hard delete wiersza).</item>
///   <item><c>PhoneConfirmationOtps</c> — OTP potwierdzenia numeru właściciela (hard delete wiersza).</item>
///   <item><c>Appointments.OtpVerification</c> — owned inline (e-mail/telefon/hash w kolumnach wiersza
///     wizyty); wiersza nie kasujemy, tylko zerujemy zapisane PII OTP.</item>
/// </list>
///
/// Retencja: <c>Otp:CleanupRetentionHours</c> (default 48). Interwał: <c>Otp:CleanupIntervalHours</c>
/// (default 1). W Testing/E2E nie startuje jako pętla (zob. Program.cs) — w E2E dostępny przez backdoor.
/// </summary>
public sealed class OtpCleanupHostedService : BackgroundService
{
  private const int BatchSize = 5000;

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<OtpCleanupHostedService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _retention;
  private readonly TimeSpan _interval;

  public OtpCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OtpCleanupHostedService> logger,
    TimeProvider timeProvider)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
    _timeProvider = timeProvider;
    var retentionHours = configuration.GetValue<double?>("Otp:CleanupRetentionHours") ?? 48d;
    var intervalHours = configuration.GetValue<double?>("Otp:CleanupIntervalHours") ?? 1d;
    _retention = TimeSpan.FromHours(retentionHours);
    _interval = TimeSpan.FromHours(intervalHours);
  }

  /// <summary>E2E / test backdoor — wymusza jeden cykl bez czekania na interwał.</summary>
  internal Task ExecuteOnceAsync(CancellationToken ct) => RunCycleAsync(ct);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunCycleAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Cleanup wygasłych OTP — błąd cyklu.");
      }

      try
      {
        await Task.Delay(_interval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }
  }

  internal async Task RunCycleAsync(CancellationToken ct)
  {
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await RunCycleAsync(db, _timeProvider.GetUtcNow().UtcDateTime, _retention, ct, _logger);
  }

  /// <summary>
  /// Testowalny entry-point: jeden cykl. <c>IgnoreQueryFilters</c> — job leci bez kontekstu
  /// tenanta, więc bez tego globalny filtr (TenantId == null / IsActive) nie złapałby niczego.
  /// <c>RemoveRange</c> / mutacja encji zamiast <c>ExecuteDelete</c> — działa na każdym providerze
  /// (także InMemory w testach). Zwraca łączną liczbę wyczyszczonych rekordów.
  /// </summary>
  internal static async Task<int> RunCycleAsync(
    ApplicationDbContext db,
    DateTime utcNow,
    TimeSpan retention,
    CancellationToken ct,
    ILogger logger)
  {
    var cutoff = utcNow - retention;
    var cleaned = 0;

    // We wszystkich trzech paczkach niżej OrderBy poprzedza Take. Bez niego baza zwraca DOWOLNY
    // podzbiór pasujących wierszy (EF sygnalizuje to ostrzeżeniem „row limiting operator without
    // OrderBy"), a kolejne cykle skaczą po zbiorze zamiast go drenować. Sortujemy po tej samej
    // kolumnie, po której filtruje cutoff — najstarsze wygaśnięcia znikają pierwsze.

    // 1) SelfServiceOtp — encja tenantowa; hard delete wiersza.
    var selfService = await db.SelfServiceOtps
      .IgnoreQueryFilters()
      .Where(o => o.ExpiresAtUtc < cutoff)
      .OrderBy(o => o.ExpiresAtUtc)
      .Take(BatchSize)
      .ToListAsync(ct);
    if (selfService.Count > 0)
    {
      db.SelfServiceOtps.RemoveRange(selfService);
      cleaned += selfService.Count;
    }

    // 2) PhoneConfirmationOtp — OTP numeru właściciela; hard delete wiersza.
    var phoneOtps = await db.PhoneConfirmationOtps
      .IgnoreQueryFilters()
      .Where(o => o.ExpiresAt < cutoff)
      .OrderBy(o => o.ExpiresAt)
      .Take(BatchSize)
      .ToListAsync(ct);
    if (phoneOtps.Count > 0)
    {
      db.PhoneConfirmationOtps.RemoveRange(phoneOtps);
      cleaned += phoneOtps.Count;
    }

    // 3) OtpVerification — owned inline w wierszu wizyty; zerujemy PII (e-mail/telefon/hash),
    //    wiersza wizyty nie kasujemy. Filtr po kolumnie owned (otp_expiry_utc).
    var staleVerifications = await db.Appointments
      .IgnoreQueryFilters()
      .Where(a => a.OtpVerification != null && a.OtpVerification.ExpiryTimeUtc < cutoff)
      .OrderBy(a => a.OtpVerification!.ExpiryTimeUtc)
      .Take(BatchSize)
      .ToListAsync(ct);
    if (staleVerifications.Count > 0)
    {
      foreach (var appointment in staleVerifications)
      {
        appointment.ClearOtpVerification();
      }
      cleaned += staleVerifications.Count;
    }

    if (cleaned > 0)
    {
      await db.SaveChangesAsync(ct);
      logger.LogInformation(
        "Cleanup wygasłych OTP: SelfService={SelfService}, Phone={Phone}, Wizyty(scrub)={Verifications} (cutoff={Cutoff:o}).",
        selfService.Count,
        phoneOtps.Count,
        staleVerifications.Count,
        cutoff);
    }

    return cleaned;
  }
}
