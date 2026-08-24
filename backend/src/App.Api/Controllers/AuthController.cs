using System.ComponentModel.DataAnnotations;
using App.Application.Auth.Commands.RegisterOwner;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Tenants.Queries.GetRegisterOwnerSlugAvailability;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Api.Options;
using App.Api.Security;
using App.Infrastructure.Email;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using MediatR;

namespace App.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
  private const string OwnerRole = "Owner";
  private const string EmployeeRole = "Employee";
  private const string ManagerRole = "Manager";

  private static readonly string[] StaffRoles = [EmployeeRole, ManagerRole];

  private readonly ApplicationDbContext _dbContext;
  private readonly UserManager<User> _userManager;
  private readonly SignInManager<User> _signInManager;
  private readonly RoleManager<IdentityRole<Guid>> _roleManager;
  private readonly IAntiforgery _antiforgery;
  private readonly IAuthEmailSender _emailSender;
  private readonly DashboardOptions _dashboardOptions;
  private readonly ITurnstileVerifier _turnstileVerifier;
  private readonly IPasswordResetEmailThrottle _passwordResetThrottle;
  private readonly IConfirmEmailResendThrottle _confirmEmailResendThrottle;
  private readonly ILogger<AuthController> _logger;
  private readonly ISender _mediator;
  private readonly ITenantVatRateSeeder _vatRateSeeder;
  private readonly IHostEnvironment _hostEnvironment;

  public AuthController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IAntiforgery antiforgery,
    IAuthEmailSender emailSender,
    IOptions<DashboardOptions> dashboardOptions,
    ITurnstileVerifier turnstileVerifier,
    IPasswordResetEmailThrottle passwordResetThrottle,
    IConfirmEmailResendThrottle confirmEmailResendThrottle,
    ISender mediator,
    ITenantVatRateSeeder vatRateSeeder,
    IHostEnvironment hostEnvironment,
    ILogger<AuthController> logger)
  {
    _dbContext = dbContext;
    _userManager = userManager;
    _signInManager = signInManager;
    _roleManager = roleManager;
    _antiforgery = antiforgery;
    _emailSender = emailSender;
    _dashboardOptions = dashboardOptions.Value;
    _turnstileVerifier = turnstileVerifier;
    _passwordResetThrottle = passwordResetThrottle;
    _confirmEmailResendThrottle = confirmEmailResendThrottle;
    _mediator = mediator;
    _vatRateSeeder = vatRateSeeder;
    _hostEnvironment = hostEnvironment;
    _logger = logger;
  }

  [AllowAnonymous]
  [HttpGet("csrf")]
  public ActionResult<CsrfTokenDto> Csrf()
  {
    var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
    Response.Cookies.Append(
      "XSRF-TOKEN",
      tokens.RequestToken ?? string.Empty,
      new CookieOptions
      {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        Secure = Request.IsHttps,
      });

    return Ok(new CsrfTokenDto(tokens.RequestToken ?? string.Empty));
  }

  /// <summary>
  /// Sprawdza, czy slug salonu jest wolny (dla formularza rejestracji właściciela). Tylko poprawny format sluga.
  /// </summary>
  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpGet("register-owner/slug-availability")]
  public async Task<ActionResult<SlugAvailabilityDto>> GetRegisterOwnerSlugAvailability(
    [FromQuery] string slug,
    CancellationToken ct)
  {
    var result = await _mediator.Send(
      new GetRegisterOwnerSlugAvailabilityQuery(slug ?? string.Empty, ExcludeCurrentTenant: false),
      ct);

    return Ok(new SlugAvailabilityDto(result.Available));
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("register-owner")]
  public async Task<ActionResult<RegisterOwnerResponse>> RegisterOwner(
    RegisterOwnerRequest request,
    CancellationToken ct)
  {
    var turnstileValid = await _turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      App.Api.Security.TurnstileWidgetKind.Managed,
      ct);
    if (!turnstileValid)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zweryfikować zabezpieczenia formularza.",
        Detail = "Odśwież stronę i spróbuj ponownie.",
      });
    }

    // Slim rejestracja: tworzy WYŁĄCZNIE konto Identity (Owner, e-mail/telefon niepotwierdzone).
    // Tenant/Employee/VAT/promo powstają dopiero w kroku kreatora (CompleteProfileCommand).
    var result = await _mediator.Send(
      new RegisterOwnerCommand(
        request.Email,
        request.Password,
        request.PhoneNumber,
        request.TurnstileToken,
        request.PromoCode,
        HttpContext.Connection.RemoteIpAddress?.ToString()),
      ct);

    // Świadomie BEZ SignInAsync — wymagamy potwierdzenia maila. Kontroler trzyma wysyłkę maila
    // (buduje URL). Cleanup job wyrzuci konto, jeśli mail/telefon nie zostaną potwierdzone w grace window.
    string? confirmEmailUrl = null;
    var user = await _userManager.FindByIdAsync(result.UserId.ToString());
    if (user != null)
    {
      try
      {
        confirmEmailUrl = await SendConfirmEmailAsync(user, ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogError(
          ex,
          "Wysłanie maila potwierdzającego do {Email} (UserId={UserId}) nie powiodło się, ale konto zostało utworzone.",
          user.Email,
          user.Id);
      }
    }

    return Ok(new RegisterOwnerResponse(
      result.UserId,
      result.Email,
      EmailConfirmed: false,
      // DEV-ONLY. Link z tokenem potwierdzającym leci wprost w odpowiedzi, żeby dało się przeklikać
      // rejestrację bez skrzynki i bez szukania go w logach (DevTools → Network).
      //
      // Bramka to IsDevelopment(), CELOWO węższa niż `!IsProduction()` przy logu wyżej: log zostaje
      // na serwerze, a TO leci po sieci do tego, kto wysłał żądanie. Poza dev oznaczałoby, że każdy
      // może zarejestrować konto na CUDZY adres i potwierdzić je bez dostępu do skrzynki — mail
      // przestaje cokolwiek dowodzić. Nie rozszerzać na LOCAL_PROD ani Staging.
      ConfirmEmailUrl: _hostEnvironment.IsDevelopment() ? confirmEmailUrl : null));
  }

  /// <summary>
  /// Admin (SystemAdmin): tworzy salon "za klienta" — tenant + konto właścicielki (Owner, e-mail
  /// potwierdzony, hasło ustawione od razu, bez maila/Turnstile/OTP) + pracownice jako zasoby kalendarza
  /// (bez logowania; konta można dodać później przez Employee.LinkToUser) + opcjonalna white-label domena.
  /// Aktywuje white-label natychmiast (refresh rejestru). Świadomie OMIJA self-signup flow (RegisterOwner).
  /// </summary>
  [Authorize(Policy = "SystemAdminOnly")]
  [HttpPost("admin/create-salon")]
  public async Task<ActionResult<AdminCreateSalonResponse>> AdminCreateSalon(
    AdminCreateSalonRequest request,
    CancellationToken ct)
  {
    if (!IsValidTimeZoneId(request.TimeZoneId))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowa strefa czasowa salonu.",
        Detail = "Podaj poprawny identyfikator strefy czasowej (np. Europe/Warsaw).",
      });
    }

    if (await _dbContext.Tenants.AnyAsync(t => t.Slug == request.SalonSlug, ct))
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Slug salonu jest już zajęty.",
      });
    }

    var normalizedDomain = string.IsNullOrWhiteSpace(request.CustomDomain)
      ? null
      : request.CustomDomain.Trim().ToLowerInvariant();
    if (normalizedDomain != null
        && await _dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.CustomDomain == normalizedDomain, ct))
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Ta domena jest już przypisana do innego salonu.",
      });
    }

    if (await _userManager.FindByEmailAsync(request.OwnerEmail) is not null)
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Konto z tym adresem e-mail już istnieje.",
      });
    }

    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    // Konto właścicielki: e-mail z góry potwierdzony (admin zakłada konto z gotowym hasłem),
    // więc logowanie działa od razu bez maila potwierdzającego.
    var owner = new User(request.OwnerEmail, request.OwnerDisplayName ?? request.OwnerEmail)
    {
      EmailConfirmed = true,
    };
    var createOwnerResult = await _userManager.CreateAsync(owner, request.OwnerPassword);
    if (!createOwnerResult.Succeeded)
    {
      return IdentityValidationProblem(createOwnerResult);
    }

    var ensureRole = await EnsureRoleExistsAsync(OwnerRole);
    if (!ensureRole.Succeeded)
    {
      return IdentityValidationProblem(ensureRole);
    }
    var addRole = await _userManager.AddToRoleAsync(owner, OwnerRole);
    if (!addRole.Succeeded)
    {
      return IdentityValidationProblem(addRole);
    }

    var tenant = new Tenant(request.SalonName, request.SalonSlug, request.TimeZoneId, request.Currency);
    tenant.StartTrial();
    tenant.SetCustomDomain(normalizedDomain);
    // Salon zakładany przez admina jest od razu w pełni skonfigurowany — pomija kreator onboardingu,
    // więc oznaczamy go jako ukończony, by guard /admin/** nie kierował właściciela do /setup.
    tenant.MarkOnboardingCompleted(DateTime.UtcNow);

    var ownerEmployee = new Employee(tenant.Id, owner.Id, request.OwnerFirstName, request.OwnerLastName, request.OwnerEmail);

    _dbContext.Tenants.Add(tenant);
    _dbContext.Employees.Add(ownerEmployee);
    _vatRateSeeder.SeedDefaults(tenant.Id, "PL");

    var staff = request.Staff ?? [];
    foreach (var member in staff)
    {
      _dbContext.Employees.Add(new Employee(tenant.Id, userId: null, member.FirstName, member.LastName, member.Email));
    }

    await _dbContext.SaveChangesAsync(ct);
    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    // Aktywuj white-label od razu (resolve-host / CORS / On-Demand TLS), bez czekania na cykliczny refresh.
    if (normalizedDomain != null)
    {
      var registry = HttpContext.RequestServices
        .GetRequiredService<App.Application.Common.Interfaces.ICustomDomainRegistry>();
      await registry.RefreshAsync(ct);
    }

    _logger.LogInformation(
      "AdminCreatedSalon TenantId={TenantId} OwnerUserId={UserId} StaffCount={StaffCount} CustomDomain={CustomDomain} Category={Category}",
      tenant.Id, owner.Id, staff.Count, normalizedDomain ?? "(none)", "audit");

    return Ok(new AdminCreateSalonResponse(tenant.Id, owner.Id));
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("login")]
  public async Task<ActionResult<AuthSessionDto>> Login(LoginRequest request, CancellationToken ct)
  {
    var turnstileValid = await _turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      App.Api.Security.TurnstileWidgetKind.Managed,
      ct);
    if (!turnstileValid)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zweryfikować zabezpieczenia formularza.",
        Detail = "Odśwież stronę i spróbuj ponownie.",
      });
    }

    var result = await _signInManager.PasswordSignInAsync(
      request.Email,
      request.Password,
      request.RememberMe,
      lockoutOnFailure: true);

    if (!result.Succeeded)
    {
      _logger.LogWarning("LoginFailed LockedOut={LockedOut} Category={Category}", result.IsLockedOut, "audit");
      return Unauthorized(new ProblemDetails
      {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Nieprawidłowy email lub hasło.",
      });
    }

    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user == null)
    {
      return Unauthorized();
    }

    // Drugi gate: telefon musi być potwierdzony — ale TYLKO gdy w ogóle jest podany. Konta
    // provisionowane przez zaufanego admina/właściciela (AdminCreateSalon, zaproszenie pracownika)
    // nie mają numeru, więc wymaganie potwierdzenia zakleszczyłoby logowanie (brak numeru → brak OTP).
    // Spójne z gate'em w confirm-email, który wysyła OTP tylko gdy numer istnieje.
    if (!user.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber))
    {
      await _signInManager.SignOutAsync();
      _logger.LogWarning(
        "LoginBlockedPhoneNotConfirmed UserId={UserId} Category={Category}", user.Id, "audit");
      throw new App.Domain.Exceptions.PhoneNotConfirmedException(user.Id);
    }

    // RememberMe decyduje o TYM, czy ciasteczko dostaje `expires` (30 dni), czy zostaje sesyjne —
    // a sesyjne ginie, gdy iOS ubija uśpioną PWA. Bez tej flagi w logu nie dało się odróżnić
    // „użytkownik nie zaznaczył" od „zaznaczył, a i tak wypadł", więc diagnoza utraconych sesji
    // stała w miejscu. Sama flaga, bez żadnych danych logowania — hasło i e-mail nie wchodzą do logu.
    _logger.LogInformation(
      "LoginSucceeded UserId={UserId} RememberMe={RememberMe} Category={Category}",
      user.Id, request.RememberMe, "audit");
    return Ok(await BuildSessionDtoAsync(user, ct));
  }

  [HttpPost("logout")]
  public async Task<IActionResult> Logout()
  {
    await _signInManager.SignOutAsync();
    _logger.LogInformation("Logout UserId={UserId} Category={Category}", GetUserId(), "audit");
    return NoContent();
  }

  [HttpGet("me")]
  public async Task<ActionResult<AuthSessionDto>> Me(CancellationToken ct)
  {
    var user = await _userManager.GetUserAsync(User);
    if (user == null)
    {
      return Unauthorized();
    }

    return Ok(await BuildSessionDtoAsync(user, ct));
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("confirm-email")]
  public async Task<ActionResult<ConfirmEmailResponse>> ConfirmEmail(
    ConfirmEmailRequest request,
    CancellationToken ct)
  {
    var user = await _userManager.FindByIdAsync(request.UserId.ToString());
    if (user == null)
    {
      return BadRequest();
    }

    var result = await _userManager.ConfirmEmailAsync(user, DecodeToken(request.Token));
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    _logger.LogInformation("EmailConfirmed UserId={UserId} Category={Category}", user.Id, "audit");

    // Po potwierdzeniu maila wysyłamy pierwszy SMS OTP (wariant C — gate antyabuzowy).
    // Jeśli wysyłka nie powiedzie się, mail i tak zostaje potwierdzony — user wywoła resend.
    var phoneOtpSent = false;
    if (!user.PhoneNumberConfirmed && !string.IsNullOrWhiteSpace(user.PhoneNumber))
    {
      try
      {
        await _mediator.Send(new App.Application.Auth.Commands.SendPhoneOtp.SendPhoneOtpCommand(user.Id), ct);
        phoneOtpSent = true;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogWarning(
          ex,
          "Wysyłka pierwszego SMS OTP po confirm-email padła dla UserId={UserId}. Email i tak potwierdzony.",
          user.Id);
      }
    }

    return Ok(new ConfirmEmailResponse(
      user.Id,
      EmailConfirmed: true,
      PhoneNumberConfirmed: user.PhoneNumberConfirmed,
      PhoneOtpSent: phoneOtpSent,
      MaskedPhone: MaskPhone(user.PhoneNumber)));
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("confirm-phone")]
  public async Task<IActionResult> ConfirmPhone(
    ConfirmPhoneRequest request,
    CancellationToken ct)
  {
    await _mediator.Send(
      new App.Application.Auth.Commands.ConfirmPhone.ConfirmPhoneCommand(request.UserId, request.Code),
      ct);
    return NoContent();
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("resend-phone-otp")]
  public async Task<IActionResult> ResendPhoneOtp(
    ResendPhoneOtpRequest request,
    CancellationToken ct)
  {
    await _mediator.Send(
      new App.Application.Auth.Commands.ResendPhoneOtp.ResendPhoneOtpCommand(request.UserId),
      ct);
    return NoContent();
  }

  private static string? MaskPhone(string? phone)
  {
    if (string.IsNullOrWhiteSpace(phone) || phone.Length < 4)
    {
      return null;
    }
    var last3 = phone[^3..];
    return "+" + new string('*', Math.Max(0, phone.Length - 4)) + last3;
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("resend-confirm-email")]
  public async Task<IActionResult> ResendConfirmEmail(ResendConfirmEmailRequest request, CancellationToken ct)
  {
    // Anti-enum: zawsze 204 niezależnie od istnienia konta, stanu confirm i throttle.
    // Per-IP rate limiter (AuthSensitive) chroni przed floodem z jednego adresu;
    // per-email throttle (IConfirmEmailResendThrottle) chroni JEDNĄ ofiarę przed
    // botnetem rozproszonym po wielu IP.
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user != null && !user.EmailConfirmed && _confirmEmailResendThrottle.TryAcquire(request.Email))
    {
      try
      {
        await SendConfirmEmailAsync(user, ct);
        _logger.LogInformation(
          "ConfirmEmailResent UserId={UserId} Category={Category}",
          user.Id,
          "audit");
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Klient i tak dostanie 204; logujemy żeby ops widziało padający mailer.
        _logger.LogError(
          ex,
          "Resend confirm-email do {Email} (UserId={UserId}) — wysyłka padła.",
          user.Email,
          user.Id);
      }
    }

    return NoContent();
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("forgot-password")]
  public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
  {
    // Cloudflare Turnstile (visible widget) — pierwsza linia obrony przed scripted abuse.
    // Bez tokenu: w prod blokujemy; w Testing/Dev (Turnstile:Enabled=false) zawsze pass.
    var turnstileValid = await _turnstileVerifier.VerifyAsync(
      request.TurnstileToken,
      HttpContext.Connection.RemoteIpAddress?.ToString(),
      App.Api.Security.TurnstileWidgetKind.Managed,
      ct);
    if (!turnstileValid)
    {
      // Anti-enum: nawet przy złym tokenie zwracamy 204 — żeby nie ujawniać heuristyk Turnstile.
      // Atak zostaje zablokowany (mail nie idzie), użytkownik nie wie czy token zły czy mail nieznany.
      return NoContent();
    }

    // Per-email throttling: chroni JEDNĄ ofiarę przed rozproszonym (botnet) spamem mailami-reset.
    // Anti-enum: zawsze 204 niezależnie od tego, czy email/throttle pozwolił na wysyłkę.
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user != null && _passwordResetThrottle.TryAcquire(request.Email))
    {
      await SendPasswordResetAsync(user, ct);
      _logger.LogInformation("PasswordResetRequested UserId={UserId} Category={Category}", user.Id, "audit");
    }

    return NoContent();
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("reset-password")]
  public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
  {
    var user = await _userManager.FindByIdAsync(request.UserId.ToString());
    if (user == null)
    {
      return BadRequest();
    }

    var result = await _userManager.ResetPasswordAsync(user, DecodeToken(request.Token), request.Password);
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    _logger.LogInformation("PasswordResetCompleted UserId={UserId} Category={Category}", user.Id, "audit");
    return NoContent();
  }

  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("employees")]
  public async Task<ActionResult<EmployeeInviteDto>> InviteEmployee(
    InviteEmployeeRequest request,
    CancellationToken ct)
  {
    if (!StaffRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowa rola pracownika.",
        Detail = "Dozwolone role to Employee lub Manager.",
      });
    }

    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    // Subscription per-seat — limit liczby pracowników wynika ze status'u i Seats.
    // Soft limit: pozwalamy zaprosić, frontend pokazuje cenę następnego cyklu po dodaniu seat'a.
    // Twardy blok wyłącznie dla nieaktywnej subskrypcji (PastDue / Canceled / wygasły Trial).
    var tenant = await _dbContext.Tenants.FindAsync([tenantId.Value], ct);
    if (tenant != null)
    {
      var effective = tenant.Subscription.EffectiveStatus;
      if (effective == App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.PastDue
          || effective == App.Domain.Aggregates.TenantAggregate.SubscriptionStatus.Canceled)
      {
        return StatusCode(StatusCodes.Status402PaymentRequired, new ProblemDetails
        {
          Status = StatusCodes.Status402PaymentRequired,
          Title = "Subskrypcja wymaga aktualizacji.",
          Detail = "Aby zapraszać pracowników, opłać kolejny okres rozliczeniowy.",
          Type = "subscription.inactive",
        });
      }
    }

    var role = StaffRoles.Single(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(displayName))
    {
      displayName = $"{request.FirstName} {request.LastName}".Trim();
    }

    var user = new User(request.Email, displayName)
    {
      EmailConfirmed = false,
    };

    var createUserResult = await _userManager.CreateAsync(user);
    if (!createUserResult.Succeeded)
    {
      return IdentityValidationProblem(createUserResult);
    }

    var ensureRoleResult = await EnsureRoleExistsAsync(role);
    if (!ensureRoleResult.Succeeded)
    {
      return IdentityValidationProblem(ensureRoleResult);
    }

    var addRoleResult = await _userManager.AddToRoleAsync(user, role);
    if (!addRoleResult.Succeeded)
    {
      return IdentityValidationProblem(addRoleResult);
    }

    var employee = new Employee(
      tenantId.Value,
      user.Id,
      request.FirstName,
      request.LastName,
      request.Email);

    _dbContext.Employees.Add(employee);
    await _dbContext.SaveChangesAsync(ct);

    await SendEmployeeInviteAsync(user, ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation(
      "EmployeeInviteSent UserId={UserId} EmployeeId={EmployeeId} TenantId={TenantId} Role={Role} Category={Category}",
      user.Id,
      employee.Id,
      tenantId.Value,
      role,
      "audit");

    return Ok(new EmployeeInviteDto(user.Id, employee.Id, role));
  }

  /// <summary>
  /// Ponowne wysłanie linku aktywacyjnego pracownikowi, który jeszcze nie potwierdził konta
  /// (link zaproszenia wygasł / zaginął). Tylko właściciel (BusinessManagement). Generuje świeży
  /// token (ważny 24h) i wysyła e-mail — bez tworzenia nowego konta. Dla już aktywnych kont zwraca 409.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("employees/{employeeId:guid}/resend-invite")]
  public async Task<IActionResult> ResendEmployeeInvite(Guid employeeId, CancellationToken ct)
  {
    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    var employee = await _dbContext.Employees
      .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId.Value, ct);
    if (employee == null)
    {
      return NotFound();
    }

    if (employee.UserId == null)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Pracownik nie ma konta logowania.",
        Detail = "Zaproszenie można wysłać tylko pracownikowi z kontem do logowania.",
      });
    }

    var user = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
    if (user == null)
    {
      return NotFound();
    }

    if (user.EmailConfirmed)
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Konto jest już aktywne.",
        Detail = "Ten pracownik potwierdził już konto — ponowne zaproszenie nie jest potrzebne.",
        Type = "employee.invite_already_confirmed",
      });
    }

    await SendEmployeeInviteAsync(user, ct);

    _logger.LogInformation(
      "EmployeeInviteResent UserId={UserId} EmployeeId={EmployeeId} TenantId={TenantId} Category={Category}",
      user.Id,
      employee.Id,
      tenantId.Value,
      "audit");

    return NoContent();
  }

  /// <summary>
  /// Zmiana roli pracownika (awans Pracownik↔Manager). Tylko właściciel (BusinessManagement).
  /// Roli właściciela nie zmienia się tutaj — do przekazania salonu służy osobny przepływ.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPut("employees/{employeeId:guid}/role")]
  public async Task<IActionResult> ChangeEmployeeRole(
    Guid employeeId,
    ChangeEmployeeRoleRequest request,
    CancellationToken ct)
  {
    if (!Roles.AssignableStaffRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowa rola pracownika.",
        Detail = "Dozwolone role to Employee lub Manager.",
      });
    }

    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    var employee = await _dbContext.Employees
      .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId.Value, ct);
    if (employee == null)
    {
      return NotFound();
    }

    if (employee.UserId == null)
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Pracownik nie ma konta logowania.",
        Detail = "Rolę można zmienić tylko pracownikowi z aktywnym kontem.",
      });
    }

    if (employee.UserId == GetUserId())
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie możesz zmienić własnej roli.",
      });
    }

    var user = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
    if (user == null)
    {
      return NotFound();
    }

    var currentRoles = await _userManager.GetRolesAsync(user);
    if (currentRoles.Contains(Roles.Owner, StringComparer.OrdinalIgnoreCase)
        || currentRoles.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie można zmienić roli właściciela.",
        Detail = "Aby przekazać salon, użyj przekazania konta właściciela.",
      });
    }

    var targetRole = Roles.AssignableStaffRoles.Single(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));

    var ensureRole = await EnsureRoleExistsAsync(targetRole);
    if (!ensureRole.Succeeded)
    {
      return IdentityValidationProblem(ensureRole);
    }

    var rolesToRemove = currentRoles
      .Where(r => Roles.AssignableStaffRoles.Contains(r, StringComparer.OrdinalIgnoreCase)
                  && !r.Equals(targetRole, StringComparison.OrdinalIgnoreCase))
      .ToArray();
    if (rolesToRemove.Length > 0)
    {
      var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
      if (!removeResult.Succeeded)
      {
        return IdentityValidationProblem(removeResult);
      }
    }

    if (!currentRoles.Contains(targetRole, StringComparer.OrdinalIgnoreCase))
    {
      var addResult = await _userManager.AddToRoleAsync(user, targetRole);
      if (!addResult.Succeeded)
      {
        return IdentityValidationProblem(addResult);
      }
    }

    _logger.LogInformation(
      "EmployeeRoleChanged EmployeeId={EmployeeId} UserId={UserId} TenantId={TenantId} Role={Role} Category={Category}",
      employee.Id, user.Id, tenantId.Value, targetRole, "audit");

    return NoContent();
  }

  /// <summary>
  /// Zmiana adresu e-mail pracownika. Tylko właściciel (BusinessManagement). Dla pracownika z kontem
  /// synchronizuje e-mail logowania (Identity: e-mail + username + znormalizowane kolumny). Zmiana
  /// inicjowana przez właściciela = wewnętrzna korekta, więc konto pozostaje potwierdzone.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPut("employees/{employeeId:guid}/email")]
  public async Task<IActionResult> ChangeEmployeeEmail(
    Guid employeeId,
    ChangeEmployeeEmailRequest request,
    CancellationToken ct)
  {
    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    var email = request.Email.Trim();

    var employee = await _dbContext.Employees
      .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId.Value, ct);
    if (employee == null)
    {
      return NotFound();
    }

    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    if (employee.UserId != null)
    {
      var user = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
      if (user == null)
      {
        return NotFound();
      }

      var setEmail = await _userManager.SetEmailAsync(user, email);
      if (!setEmail.Succeeded)
      {
        return IdentityValidationProblem(setEmail);
      }

      var setUserName = await _userManager.SetUserNameAsync(user, email);
      if (!setUserName.Succeeded)
      {
        return IdentityValidationProblem(setUserName);
      }

      user.EmailConfirmed = true;
      var updateUser = await _userManager.UpdateAsync(user);
      if (!updateUser.Succeeded)
      {
        return IdentityValidationProblem(updateUser);
      }
    }

    employee.UpdateEmail(email);
    await _dbContext.SaveChangesAsync(ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation(
      "EmployeeEmailChanged EmployeeId={EmployeeId} TenantId={TenantId} Category={Category}",
      employee.Id, tenantId.Value, "audit");

    return NoContent();
  }

  /// <summary>
  /// Przekazanie salonu nowemu właścicielowi (tylko SystemAdmin). Przepina e-mail istniejącego
  /// konta właściciela na nowy adres (zachowuje EmployeeId i całą historię salonu) i wysyła link do
  /// ustawienia hasła. Konto właściciela pozostaje tym samym rekordem — zmienia się tylko login.
  /// </summary>
  [Authorize(Policy = "SystemAdminOnly")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("admin/transfer-ownership")]
  public async Task<IActionResult> TransferOwnership(TransferOwnershipRequest request, CancellationToken ct)
  {
    var email = request.NewEmail.Trim();

    var ownerRoleId = await _dbContext.Roles
      .Where(r => r.Name == Roles.Owner)
      .Select(r => r.Id)
      .FirstOrDefaultAsync(ct);
    if (ownerRoleId == Guid.Empty)
    {
      return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Brak roli właściciela." });
    }

    var candidateUserIds = await _dbContext.Employees
      .IgnoreQueryFilters()
      .Where(e => e.TenantId == request.TenantId && e.IsActive && e.UserId != null)
      .Select(e => e.UserId!.Value)
      .ToListAsync(ct);

    var ownerUserId = await _dbContext.UserRoles
      .Where(ur => ur.RoleId == ownerRoleId && candidateUserIds.Contains(ur.UserId))
      .Select(ur => ur.UserId)
      .FirstOrDefaultAsync(ct);
    if (ownerUserId == Guid.Empty)
    {
      return NotFound(new ProblemDetails
      {
        Status = StatusCodes.Status404NotFound,
        Title = "Salon nie ma właściciela z kontem logowania.",
      });
    }

    var user = await _userManager.FindByIdAsync(ownerUserId.ToString());
    if (user == null)
    {
      return NotFound();
    }

    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    var setEmail = await _userManager.SetEmailAsync(user, email);
    if (!setEmail.Succeeded)
    {
      return IdentityValidationProblem(setEmail);
    }

    var setUserName = await _userManager.SetUserNameAsync(user, email);
    if (!setUserName.Succeeded)
    {
      return IdentityValidationProblem(setUserName);
    }

    user.EmailConfirmed = true;
    var updateUser = await _userManager.UpdateAsync(user);
    if (!updateUser.Succeeded)
    {
      return IdentityValidationProblem(updateUser);
    }

    var ownerEmployees = await _dbContext.Employees
      .IgnoreQueryFilters()
      .Where(e => e.TenantId == request.TenantId && e.UserId == ownerUserId)
      .ToListAsync(ct);
    foreach (var emp in ownerEmployees)
    {
      emp.UpdateEmail(email);
    }
    await _dbContext.SaveChangesAsync(ct);

    await SendPasswordResetAsync(user, ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation(
      "OwnershipTransferred TenantId={TenantId} NewOwnerUserId={UserId} Category={Category}",
      request.TenantId, user.Id, "audit");

    return NoContent();
  }

  /// <summary>
  /// Włącza logowanie istniejącej pracowniczce-zasobowi (<c>userId == null</c>): tworzy konto Identity
  /// z podanym e-mailem + rolą, podpina je do istniejącego rekordu (<c>LinkToUser</c>) — bez duplikatu,
  /// z zachowaniem kalendarza/wizyt — i wysyła link „ustaw hasło". SystemAdmin (onboarding salonu).
  /// </summary>
  [Authorize(Policy = "SystemAdminOnly")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("admin/tenants/{tenantId:guid}/employees/{employeeId:guid}/enable-login")]
  public async Task<IActionResult> EnableEmployeeLogin(
    Guid tenantId,
    Guid employeeId,
    EnableEmployeeLoginRequest request,
    CancellationToken ct)
  {
    if (!Roles.AssignableStaffRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowa rola pracownika.",
        Detail = "Dozwolone role to Employee lub Manager.",
      });
    }

    var employee = await _dbContext.Employees
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId && e.IsActive, ct);
    if (employee == null)
    {
      return NotFound();
    }

    if (employee.UserId != null)
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Pracownik ma już konto logowania.",
        Detail = "Aby zmienić adres logowania, użyj zmiany e-maila.",
      });
    }

    var email = request.Email.Trim();
    if (await _userManager.FindByEmailAsync(email) != null)
    {
      return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
      {
        ["DuplicateEmail"] = new[] { "Email is already taken." },
      })
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się utworzyć konta.",
      });
    }

    var role = Roles.AssignableStaffRoles.Single(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
    var displayName = $"{employee.FirstName} {employee.LastName}".Trim();

    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    var user = new User(email, displayName) { EmailConfirmed = false };
    var createUser = await _userManager.CreateAsync(user);
    if (!createUser.Succeeded)
    {
      return IdentityValidationProblem(createUser);
    }

    var ensureRole = await EnsureRoleExistsAsync(role);
    if (!ensureRole.Succeeded)
    {
      return IdentityValidationProblem(ensureRole);
    }

    var addRole = await _userManager.AddToRoleAsync(user, role);
    if (!addRole.Succeeded)
    {
      return IdentityValidationProblem(addRole);
    }

    employee.LinkToUser(user.Id);
    employee.UpdateEmail(email);
    await _dbContext.SaveChangesAsync(ct);

    await SendEmployeeInviteAsync(user, ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation(
      "EmployeeLoginEnabled TenantId={TenantId} EmployeeId={EmployeeId} UserId={UserId} Role={Role} Category={Category}",
      tenantId, employee.Id, user.Id, role, "audit");

    return NoContent();
  }

  /// <summary>Zmiana własnej nazwy wyświetlanej (dowolny zalogowany użytkownik).</summary>
  [Authorize]
  [HttpPost("account/profile")]
  public async Task<IActionResult> UpdateMyProfile(UpdateMyProfileRequest request, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    var user = await _userManager.FindByIdAsync(userId.Value.ToString());
    if (user == null)
    {
      return Unauthorized();
    }

    // UpdateProfile ustawia Email+UserName+DisplayName — e-mail zostawiamy bez zmian, ruszamy tylko nazwę.
    user.UpdateProfile(user.Email ?? string.Empty, request.DisplayName.Trim());
    var result = await _userManager.UpdateAsync(user);
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    return NoContent();
  }

  /// <summary>Zmiana własnego hasła (wymaga podania obecnego).</summary>
  [Authorize]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("account/password")]
  public async Task<IActionResult> ChangeMyPassword(ChangeMyPasswordRequest request, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    var user = await _userManager.FindByIdAsync(userId.Value.ToString());
    if (user == null)
    {
      return Unauthorized();
    }

    var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    _logger.LogInformation("AccountPasswordChanged UserId={UserId} Category={Category}", user.Id, "audit");
    return NoContent();
  }

  /// <summary>
  /// Krok 1 zmiany własnego e-maila: weryfikuje obecne hasło i wysyła link potwierdzający na NOWY
  /// adres. E-mail zmienia się dopiero po kliknięciu linku (<c>account/confirm-change-email</c>) —
  /// dowód kontroli nad nową skrzynką. Zajęty adres → 400 z czytelnym kodem (zamiast martwego linku).
  /// </summary>
  [Authorize]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("account/email")]
  public async Task<IActionResult> ChangeMyEmail(ChangeMyEmailRequest request, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    var user = await _userManager.FindByIdAsync(userId.Value.ToString());
    if (user == null)
    {
      return Unauthorized();
    }

    if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
    {
      return BadRequest(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowe hasło.",
      });
    }

    var email = request.NewEmail.Trim();

    var existing = await _userManager.FindByEmailAsync(email);
    if (existing != null && existing.Id != user.Id)
    {
      return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
      {
        ["DuplicateEmail"] = new[] { "Email is already taken." },
      })
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nie udało się zmienić adresu e-mail.",
      });
    }

    var token = await _userManager.GenerateChangeEmailTokenAsync(user, email);
    var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    var url = $"{_dashboardOptions.BaseUrl.TrimEnd('/')}/confirm-change-email"
            + $"?userId={user.Id}&token={encodedToken}&email={Uri.EscapeDataString(email)}";

    await _emailSender.SendChangeEmailConfirmationAsync(email, url, ct);
    if (!_hostEnvironment.IsProduction())
    {
      _logger.LogInformation("[DEV] Change-email confirm URL: {Url}", url);
    }

    _logger.LogInformation("AccountEmailChangeRequested UserId={UserId} Category={Category}", user.Id, "audit");
    return NoContent();
  }

  /// <summary>
  /// Krok 2: potwierdzenie zmiany e-maila z linku (token wygenerowany dla pary user + nowy e-mail).
  /// Przepina e-mail + username (Identity) i synchronizuje rekordy pracownika. AllowAnonymous — token
  /// jest dowodem; link może zostać otwarty na innym urządzeniu niż zalogowana sesja.
  /// </summary>
  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("account/confirm-change-email")]
  public async Task<IActionResult> ConfirmChangeEmail(ConfirmChangeEmailRequest request, CancellationToken ct)
  {
    var user = await _userManager.FindByIdAsync(request.UserId.ToString());
    if (user == null)
    {
      return BadRequest();
    }

    var email = request.Email.Trim();
    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    // ChangeEmailAsync ustawia Email + EmailConfirmed=true na sukces; username synchronizujemy osobno.
    var changeEmail = await _userManager.ChangeEmailAsync(user, email, DecodeToken(request.Token));
    if (!changeEmail.Succeeded)
    {
      return IdentityValidationProblem(changeEmail);
    }

    var setUserName = await _userManager.SetUserNameAsync(user, email);
    if (!setUserName.Succeeded)
    {
      return IdentityValidationProblem(setUserName);
    }

    var employees = await _dbContext.Employees
      .IgnoreQueryFilters()
      .Where(e => e.UserId == user.Id)
      .ToListAsync(ct);
    foreach (var emp in employees)
    {
      emp.UpdateEmail(email);
    }
    await _dbContext.SaveChangesAsync(ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation("AccountEmailChanged UserId={UserId} Category={Category}", user.Id, "audit");
    return NoContent();
  }

  /// <summary>Status konta „Recepcja" (kiosk) bieżącego salonu — czy istnieje i pod jakim e-mailem.</summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpGet("kiosk")]
  public async Task<ActionResult<KioskAccountDto>> GetKioskAccount(CancellationToken ct)
  {
    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    var kiosk = await FindKioskAsync(tenantId.Value, ct);
    return Ok(kiosk == null
      ? new KioskAccountDto(false, null)
      : new KioskAccountDto(true, kiosk.Value.user.Email));
  }

  /// <summary>
  /// Tworzy konto „Recepcja" (kiosk) dla salonu: konto logowania w roli Kiosk + nierezerwowalny
  /// rekord pracownika (bez własnego kalendarza). Konto obsługuje wizyty całego zespołu, ale nie ma
  /// dostępu do ustawień, pracowników ani subskrypcji. Jedno konto recepcji na salon.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("kiosk")]
  public async Task<ActionResult<KioskAccountDto>> CreateKioskAccount(CreateKioskAccountRequest request, CancellationToken ct)
  {
    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    if (await FindKioskAsync(tenantId.Value, ct) != null)
    {
      return Conflict(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Konto recepcji już istnieje.",
        Detail = "Zresetuj hasło zamiast tworzyć nowe konto.",
      });
    }

    var email = request.Email.Trim();
    await using var transaction = await BeginTransactionIfRelationalAsync(ct);

    var user = new User(email, "Recepcja")
    {
      EmailConfirmed = true,
      // Kiosk nie przechodzi bramek potwierdzeń (brak telefonu) — ustawiamy je wprost, by mógł się zalogować.
      PhoneNumberConfirmed = true,
    };

    var createUser = await _userManager.CreateAsync(user, request.Password);
    if (!createUser.Succeeded)
    {
      return IdentityValidationProblem(createUser);
    }

    var ensureRole = await EnsureRoleExistsAsync(Roles.Kiosk);
    if (!ensureRole.Succeeded)
    {
      return IdentityValidationProblem(ensureRole);
    }

    var addRole = await _userManager.AddToRoleAsync(user, Roles.Kiosk);
    if (!addRole.Succeeded)
    {
      return IdentityValidationProblem(addRole);
    }

    var employee = new Employee(tenantId.Value, user.Id, "Recepcja", "Salon", email);
    employee.MakeNonBookable();
    _dbContext.Employees.Add(employee);
    await _dbContext.SaveChangesAsync(ct);

    if (transaction != null)
    {
      await transaction.CommitAsync(ct);
    }

    _logger.LogInformation(
      "KioskAccountCreated TenantId={TenantId} UserId={UserId} Category={Category}",
      tenantId.Value, user.Id, "audit");

    return Ok(new KioskAccountDto(true, email));
  }

  /// <summary>Reset hasła konta „Recepcja" (np. po utracie laptopa) — tylko właściciel.</summary>
  [Authorize(Policy = "BusinessManagement")]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("kiosk/password")]
  public async Task<IActionResult> ResetKioskPassword(ResetKioskPasswordRequest request, CancellationToken ct)
  {
    var tenantId = GetResolvedTenantId();
    if (tenantId == null)
    {
      return Forbid();
    }

    var kiosk = await FindKioskAsync(tenantId.Value, ct);
    if (kiosk == null)
    {
      return NotFound(new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Brak konta recepcji." });
    }

    var user = kiosk.Value.user;
    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
    var result = await _userManager.ResetPasswordAsync(user, token, request.Password);
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    _logger.LogInformation("KioskPasswordReset TenantId={TenantId} UserId={UserId} Category={Category}",
      tenantId.Value, user.Id, "audit");
    return NoContent();
  }

  [AllowAnonymous]
  [EnableRateLimiting("AuthSensitive")]
  [HttpPost("accept-invite")]
  public async Task<ActionResult<AuthSessionDto>> AcceptInvite(AcceptInviteRequest request, CancellationToken ct)
  {
    var user = await _userManager.FindByIdAsync(request.UserId.ToString());
    if (user == null)
    {
      return BadRequest();
    }

    var result = await _userManager.ResetPasswordAsync(user, DecodeToken(request.Token), request.Password);
    if (!result.Succeeded)
    {
      return IdentityValidationProblem(result);
    }

    user.EmailConfirmed = true;
    await _userManager.UpdateAsync(user);
    await _signInManager.SignInAsync(user, isPersistent: false);

    _logger.LogInformation("EmployeeInviteAccepted UserId={UserId} Category={Category}", user.Id, "audit");
    return Ok(await BuildSessionDtoAsync(user, ct));
  }

  private async Task<AuthSessionDto> BuildSessionDtoAsync(User user, CancellationToken ct)
  {
    var roles = await _userManager.GetRolesAsync(user);

    // Support impersonation: gdy middleware ustawił sesję wsparcia, prezentujemy frontendowi
    // kontekst DOCELOWEGO salonu z rolą Owner — cały dashboard renderuje się jak dla właściciela,
    // a baner sygnalizuje tryb wsparcia. Realna autoryzacja API dalej opiera się na cookie/claimie
    // Admin (to DTO jest wyłącznie dla UI).
    if (HttpContext.Items.TryGetValue(ImpersonationHttpContextKeys.SessionId, out _)
        && GetResolvedTenantId() is { } impersonatedTenantId)
    {
      var tenantName = await _dbContext.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(t => t.Id == impersonatedTenantId)
        .Select(t => t.Name)
        .FirstOrDefaultAsync(ct);

      return new AuthSessionDto(
        user.Id,
        user.Email ?? string.Empty,
        user.DisplayName,
        new[] { "Owner" },
        impersonatedTenantId,
        EmployeeId: null,
        IsDemo: false,
        IsImpersonating: true,
        ImpersonatedTenantName: tenantName);
    }

    var employee = await _dbContext.Employees
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(e => e.UserId == user.Id && e.IsActive)
      .Select(e => new { e.Id, e.TenantId })
      .FirstOrDefaultAsync(ct);

    var isDemo = employee != null
      && await _dbContext.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(t => t.Id == employee.TenantId)
        .Select(t => t.IsDemo)
        .FirstOrDefaultAsync(ct);

    return new AuthSessionDto(
      user.Id,
      user.Email ?? string.Empty,
      user.DisplayName,
      roles.ToArray(),
      employee?.TenantId,
      employee?.Id,
      isDemo);
  }

  /// <summary>Znajduje konto „Recepcja" (kiosk) salonu: aktywny pracownik z powiązanym userem w roli Kiosk.</summary>
  private async Task<(Employee employee, User user)?> FindKioskAsync(Guid tenantId, CancellationToken ct)
  {
    var kioskRoleId = await _dbContext.Roles
      .Where(r => r.Name == Roles.Kiosk)
      .Select(r => r.Id)
      .FirstOrDefaultAsync(ct);
    if (kioskRoleId == Guid.Empty)
    {
      return null;
    }

    var kioskUserIds = _dbContext.UserRoles.Where(ur => ur.RoleId == kioskRoleId).Select(ur => ur.UserId);

    var employee = await _dbContext.Employees
      .IgnoreQueryFilters()
      .Where(e => e.TenantId == tenantId && e.IsActive && e.UserId != null && kioskUserIds.Contains(e.UserId!.Value))
      .FirstOrDefaultAsync(ct);
    if (employee?.UserId == null)
    {
      return null;
    }

    var user = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
    return user == null ? null : (employee, user);
  }

  private Guid? GetResolvedTenantId()
  {
    return HttpContext.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedTenantId, out var value)
      && value is Guid tenantId
        ? tenantId
        : null;
  }

  private Guid? GetUserId()
  {
    var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(value, out var id) ? id : null;
  }

  private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(CancellationToken ct)
  {
    return _dbContext.Database.IsRelational()
      ? await _dbContext.Database.BeginTransactionAsync(ct)
      : null;
  }

  private async Task<IdentityResult> EnsureRoleExistsAsync(string role)
  {
    if (await _roleManager.RoleExistsAsync(role))
    {
      return IdentityResult.Success;
    }

    return await _roleManager.CreateAsync(new IdentityRole<Guid>
    {
      Id = Guid.NewGuid(),
      Name = role,
    });
  }

  private BadRequestObjectResult IdentityValidationProblem(IdentityResult result)
  {
    // Zwracamy KODY błędów Identity (klucze) — tłumaczenie na polski robi frontend
    // (core/errors/api-error-messages.ts), spójnie z resztą komunikatów API.
    var errors = result.Errors
      .GroupBy(error => error.Code)
      .ToDictionary(
        group => group.Key,
        group => group.Select(error => error.Description).ToArray());

    return BadRequest(new ValidationProblemDetails(errors)
    {
      Status = StatusCodes.Status400BadRequest,
      Title = "Nie udało się utworzyć konta użytkownika.",
    });
  }

  /// <summary>Wysyła mail potwierdzający i zwraca zbudowany URL (dev używa go do podglądu).</summary>
  private async Task<string> SendConfirmEmailAsync(User user, CancellationToken ct)
  {
    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
    var url = BuildAuthUrl("/confirm-email", user.Id, token);
    if (!_hostEnvironment.IsProduction())
    {
      _logger.LogInformation("[DEV] Confirm-email URL: {Url}", url);
    }
    await _emailSender.SendConfirmEmailAsync(user.Email ?? string.Empty, url, ct);
    return url;
  }

  private async Task SendPasswordResetAsync(User user, CancellationToken ct)
  {
    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
    var url = BuildAuthUrl("/reset-password", user.Id, token);
    await _emailSender.SendPasswordResetAsync(user.Email ?? string.Empty, url, ct);
  }

  private async Task SendEmployeeInviteAsync(User user, CancellationToken ct)
  {
    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
    var url = BuildAuthUrl("/accept-invite", user.Id, token);
    if (!_hostEnvironment.IsProduction())
    {
      _logger.LogInformation("[DEV] Accept-invite URL: {Url}", url);
    }
    await _emailSender.SendEmployeeInviteAsync(user.Email ?? string.Empty, url, ct);
  }

  private string BuildAuthUrl(string path, Guid userId, string token)
  {
    var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    return $"{_dashboardOptions.BaseUrl.TrimEnd('/')}{path}?userId={userId}&token={encodedToken}";
  }

  private static string DecodeToken(string token) =>
    Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

  private static bool IsValidTimeZoneId(string timeZoneId)
  {
    try
    {
      _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
      return true;
    }
    catch
    {
      return false;
    }
  }
}

public sealed record CsrfTokenDto(string Token);

public sealed record SlugAvailabilityDto(bool Available);

public sealed record RegisterOwnerRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [Required, MinLength(8)] string Password,
  [Required, MaxLength(32)] string PhoneNumber,
  [MaxLength(2048)] string? TurnstileToken = null,
  /// <summary>Opcjonalny kod promocyjny. Przechowywany na koncie i redempowany przy tworzeniu tenanta w kreatorze.</summary>
  [MaxLength(64)] string? PromoCode = null);

public sealed record LoginRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [Required] string Password,
  bool RememberMe = false,
  [MaxLength(2048)] string? TurnstileToken = null);

public sealed record ConfirmEmailRequest(Guid UserId, [Required] string Token);

public sealed record ConfirmEmailResponse(
  Guid UserId,
  bool EmailConfirmed,
  bool PhoneNumberConfirmed,
  bool PhoneOtpSent,
  string? MaskedPhone);

public sealed record ConfirmPhoneRequest(
  Guid UserId,
  [Required, RegularExpression(@"^\d{6}$")] string Code);

public sealed record ResendPhoneOtpRequest(Guid UserId);

public sealed record ForgotPasswordRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [MaxLength(2048)] string? TurnstileToken = null);

public sealed record ResendConfirmEmailRequest(
  [Required, EmailAddress, MaxLength(255)] string Email);

public sealed record ResetPasswordRequest(Guid UserId, [Required] string Token, [Required, MinLength(8)] string Password);

public sealed record InviteEmployeeRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [MaxLength(200)] string? DisplayName,
  [Required, MaxLength(100)] string FirstName,
  [Required, MaxLength(100)] string LastName,
  [Required] string Role);

public sealed record ChangeEmployeeRoleRequest([Required] string Role);

public sealed record ChangeEmployeeEmailRequest([Required, EmailAddress, MaxLength(255)] string Email);

public sealed record TransferOwnershipRequest(
  [Required] Guid TenantId,
  [Required, EmailAddress, MaxLength(255)] string NewEmail);

public sealed record EnableEmployeeLoginRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [Required] string Role);

public sealed record UpdateMyProfileRequest([Required, MaxLength(200)] string DisplayName);

public sealed record ChangeMyPasswordRequest(
  [Required] string CurrentPassword,
  [Required, MinLength(8)] string NewPassword);

public sealed record ChangeMyEmailRequest(
  [Required] string CurrentPassword,
  [Required, EmailAddress, MaxLength(255)] string NewEmail);

public sealed record ConfirmChangeEmailRequest(
  [Required] Guid UserId,
  [Required] string Token,
  [Required, EmailAddress, MaxLength(255)] string Email);

public sealed record CreateKioskAccountRequest(
  [Required, EmailAddress, MaxLength(255)] string Email,
  [Required, MinLength(8)] string Password);

public sealed record ResetKioskPasswordRequest([Required, MinLength(8)] string Password);

public sealed record KioskAccountDto(bool Exists, string? Email);

public sealed record AcceptInviteRequest(Guid UserId, [Required] string Token, [Required, MinLength(8)] string Password);

public sealed record AuthSessionDto(
  Guid UserId,
  string Email,
  string DisplayName,
  IReadOnlyCollection<string> Roles,
  Guid? TenantId,
  Guid? EmployeeId,
  bool IsDemo = false,
  bool IsImpersonating = false,
  string? ImpersonatedTenantName = null);

/// <param name="ConfirmEmailUrl">
/// DEV-ONLY podgląd linku potwierdzającego — poza <c>Development</c> zawsze <c>null</c>.
/// Istnieje wyłącznie po to, żeby przeklikać rejestrację bez skrzynki pocztowej.
/// Poza dev wystawienie tego linku pozwoliłoby potwierdzić CUDZY adres e-mail bez dostępu
/// do skrzynki — patrz komentarz przy jego ustawianiu w <c>RegisterOwner</c>.
/// </param>
public sealed record RegisterOwnerResponse(
  Guid UserId,
  string Email,
  bool EmailConfirmed,
  string? ConfirmEmailUrl = null);

public sealed record EmployeeInviteDto(Guid UserId, Guid EmployeeId, string Role);

public sealed record AdminCreateSalonStaffMember(
  [Required, MaxLength(100)] string FirstName,
  [Required, MaxLength(100)] string LastName,
  [Required, EmailAddress, MaxLength(255)] string Email);

public sealed record AdminCreateSalonRequest(
  [Required, MaxLength(100)] string SalonName,
  [Required, MaxLength(100), RegularExpression("^[a-zA-Z0-9-]+$")] string SalonSlug,
  [Required, MaxLength(100)] string TimeZoneId,
  [Required, MinLength(3), MaxLength(3), RegularExpression("^[A-Za-z]{3}$")] string Currency,
  [Required, EmailAddress, MaxLength(255)] string OwnerEmail,
  [Required, MinLength(8)] string OwnerPassword,
  [Required, MaxLength(100)] string OwnerFirstName,
  [Required, MaxLength(100)] string OwnerLastName,
  [MaxLength(200)] string? OwnerDisplayName = null,
  [MaxLength(253)] string? CustomDomain = null,
  IReadOnlyList<AdminCreateSalonStaffMember>? Staff = null);

public sealed record AdminCreateSalonResponse(Guid TenantId, Guid OwnerUserId);
