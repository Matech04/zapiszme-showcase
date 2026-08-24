using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

/// <summary>
/// Publiczny tryb demo — pozwala odwiedzającemu wypróbować panel bez rejestracji i logowania.
/// <c>POST start</c> tworzy efemerycznego, w pełni izolowanego tenanta (własny salon SOLO
/// z danymi), loguje gościa jego cookie sesyjnym i zwraca <see cref="AuthSessionDto"/> — od tego
/// momentu działa cały dashboard tak jak dla zwykłego właściciela.
///
/// Demo-tenanty są oznaczone <see cref="Tenant.IsDemo"/>: nie wysyłają realnych powiadomień
/// i są hard-kasowane przez <c>DemoTenantCleanupHostedService</c> po TTL.
/// </summary>
[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
  private const string OwnerRole = "Owner";

  private readonly ApplicationDbContext _dbContext;
  private readonly UserManager<User> _userManager;
  private readonly SignInManager<User> _signInManager;
  private readonly RoleManager<IdentityRole<Guid>> _roleManager;
  private readonly IDemoDataSeeder _demoDataSeeder;
  private readonly TimeProvider _timeProvider;
  private readonly IConfiguration _configuration;
  private readonly ILogger<DemoController> _logger;

  public DemoController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IDemoDataSeeder demoDataSeeder,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<DemoController> logger)
  {
    _dbContext = dbContext;
    _userManager = userManager;
    _signInManager = signInManager;
    _roleManager = roleManager;
    _demoDataSeeder = demoDataSeeder;
    _timeProvider = timeProvider;
    _configuration = configuration;
    _logger = logger;
  }

  /// <summary>Czy publiczny tryb demo jest włączony (frontend pokazuje/ukrywa przycisk „Wypróbuj demo").</summary>
  [AllowAnonymous]
  [HttpGet("enabled")]
  public ActionResult<DemoEnabledDto> Enabled() => Ok(new DemoEnabledDto(IsDemoEnabled));

  /// <summary>
  /// Tworzy nowego demo-tenanta z danymi, loguje gościa i zwraca sesję. Anonimowy, rate-limited.
  /// </summary>
  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("start")]
  public async Task<ActionResult<AuthSessionDto>> Start(CancellationToken ct)
  {
    if (!IsDemoEnabled)
    {
      return NotFound();
    }

    // Twardy limit liczby żywych demo-tenantów — anty-abuse (cleanup zwalnia miejsca po TTL).
    var liveDemoCount = await _dbContext.Tenants.CountAsync(t => t.IsDemo, ct);
    if (liveDemoCount >= MaxLiveDemoTenants)
    {
      _logger.LogWarning(
        "DemoStartRejected — osiągnięto limit żywych demo-tenantów ({Count}/{Max}).",
        liveDemoCount, MaxLiveDemoTenants);
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
      {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title = "Tryb demo jest chwilowo przeciążony.",
        Detail = "Spróbuj ponownie za kilka minut.",
      });
    }

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
    var token = Guid.NewGuid().ToString("N")[..8];
    var slug = $"demo-{token}";
    var email = $"demo-{token}@demo.zapisz.me";

    await using var transaction = _dbContext.Database.IsRelational()
      ? await _dbContext.Database.BeginTransactionAsync(ct)
      : null;

    // Passwordless — gość nigdy się nie loguje hasłem, wchodzi przez SignInAsync poniżej.
    // Flagi confirmed=true, żeby konto nie wpadło w cleanup niepotwierdzonych rejestracji
    // (demo ma własny, szybszy cleanup po IsDemo).
    var user = new User(email, "Ania (demo)")
    {
      EmailConfirmed = true,
      PhoneNumberConfirmed = true,
    };

    var createUserResult = await _userManager.CreateAsync(user);
    if (!createUserResult.Succeeded)
    {
      _logger.LogError(
        "DemoStartFailed — nie udało się utworzyć usera demo: {Errors}",
        string.Join("; ", createUserResult.Errors.Select(e => e.Description)));
      return Problem("Nie udało się uruchomić demo. Spróbuj ponownie.");
    }

    await EnsureRoleExistsAsync(OwnerRole);
    await _userManager.AddToRoleAsync(user, OwnerRole);

    var tenant = new Tenant("Studio Stylizacji (DEMO)", slug, "Europe/Warsaw", "PLN");
    tenant.MarkAsDemo(nowUtc);
    tenant.StartTrial();
    // Demo jest w pełni zaseedowane (usługi, grafik, wizyty) — pomija kreator, więc od razu ukończone.
    tenant.MarkOnboardingCompleted(nowUtc);

    var owner = new Employee(tenant.Id, user.Id, "Ania", "Demo", email);

    _dbContext.Tenants.Add(tenant);
    _dbContext.Employees.Add(owner);

    _demoDataSeeder.Seed(_dbContext, tenant, owner, nowUtc);

    await _dbContext.SaveChangesAsync(ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    // Wystawia cookie sesyjne — od teraz gość jest „zalogowany" jako właściciel demo-salonu.
    await _signInManager.SignInAsync(user, isPersistent: false);

    _logger.LogInformation(
      "DemoStarted UserId={UserId} TenantId={TenantId} Slug={Slug} Category={Category}",
      user.Id, tenant.Id, slug, "audit");

    var roles = await _userManager.GetRolesAsync(user);
    return Ok(new AuthSessionDto(
      user.Id,
      email,
      user.DisplayName,
      roles.ToArray(),
      tenant.Id,
      owner.Id,
      IsDemo: true));
  }

  private bool IsDemoEnabled => _configuration.GetValue<bool?>("Demo:Enabled") ?? false;

  private int MaxLiveDemoTenants => _configuration.GetValue<int?>("Demo:MaxLiveTenants") ?? 200;

  private async Task EnsureRoleExistsAsync(string role)
  {
    if (!await _roleManager.RoleExistsAsync(role))
    {
      await _roleManager.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = role });
    }
  }
}

public sealed record DemoEnabledDto(bool Enabled);
