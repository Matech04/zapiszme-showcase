using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Auth.Commands.RegisterOwner;

/// <summary>
/// Slim rejestracja właściciela (Droga B): tworzy WYŁĄCZNIE konto Identity w roli Owner z
/// niepotwierdzonym e-mailem i telefonem. Tenant/Employee/VAT/promo powstają dopiero w kroku
/// kreatora (<c>CompleteProfileCommand</c>). Opcjonalny kod promo jest przechowywany na koncie
/// (<see cref="User.PendingPromoCode"/>) do czasu utworzenia tenanta.
///
/// UWAGA: weryfikacja Turnstile i wysyłka maila potwierdzającego zostają w kontrolerze — to
/// koncerny brzegu HTTP (Turnstile potrzebuje IP z HttpContext i żyje w App.Api; budowa URL-a
/// maila jest w kontrolerze). Handler nie zależy od App.Api.
/// </summary>
public sealed record RegisterOwnerCommand(
  string Email,
  string Password,
  string PhoneNumber,
  string? TurnstileToken,
  string? PromoCode,
  string? RemoteIp) : IRequest<RegisterOwnerResult>;

public sealed record RegisterOwnerResult(Guid UserId, string Email);

public sealed class RegisterOwnerCommandValidator : AbstractValidator<RegisterOwnerCommand>
{
  public RegisterOwnerCommandValidator()
  {
    RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
    RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
    RuleFor(x => x.PromoCode).MaximumLength(64);
  }
}

internal sealed class RegisterOwnerCommandHandler
  : IRequestHandler<RegisterOwnerCommand, RegisterOwnerResult>
{
  private const string OwnerRole = Roles.Owner;

  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<IdentityRole<Guid>> _roleManager;
  private readonly ILogger<RegisterOwnerCommandHandler> _logger;

  public RegisterOwnerCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    UserManager<User> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ILogger<RegisterOwnerCommandHandler> logger)
  {
    _context = context;
    _uow = uow;
    _userManager = userManager;
    _roleManager = roleManager;
    _logger = logger;
  }

  public async Task<RegisterOwnerResult> Handle(RegisterOwnerCommand request, CancellationToken ct)
  {
    // Walidacja + normalizacja numeru do E.164. PhoneNumber VO rzuca ArgumentException → 400.
    string normalizedPhone;
    var parsedPhone = new PhoneNumber(request.PhoneNumber);
    // Anti toll-fraud: rejestracja wysyła OTP SMS, więc dopuszczamy WYŁĄCZNIE numery polskie (+48).
    if (!parsedPhone.IsPolish)
    {
      throw new UnsupportedPhoneRegionException();
    }
    normalizedPhone = parsedPhone.Value;

    // Defense-in-depth: unikalność e-maila i telefonu PRZED CreateAsync. Generyczny 409 bez
    // wskazania pola (anti-enumeration).
    var emailTaken = await _userManager.FindByEmailAsync(request.Email) is not null;
    var phoneTaken = await _context.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone, ct);
    if (emailTaken || phoneTaken)
    {
      throw new RegistrationConflictException();
    }

    // DisplayName = e-mail (placeholder). Prawdziwa nazwa jest ustawiana w CompleteProfile.
    var user = new User(request.Email, request.Email)
    {
      EmailConfirmed = false,
      PhoneNumber = normalizedPhone,
      PhoneNumberConfirmed = false,
    };

    // Odroczony kod promo — redempcja wymaga tenanta (powstaje w CompleteProfile), więc tylko zapis.
    // Ustawiony PRZED CreateAsync, żeby utrwalić się w jednym zapisie konta.
    user.SetPendingPromoCode(request.PromoCode);

    // Atomowo: konto + rola. UserManager zapisuje w tym samym scoped DbContext, więc udział w transakcji.
    await _uow.ExecuteInTransactionAsync(async _ =>
    {
      var createUserResult = await _userManager.CreateAsync(user, request.Password);
      if (!createUserResult.Succeeded)
      {
        throw ToIdentityException(createUserResult);
      }

      await EnsureRoleExistsAsync(OwnerRole);

      var addRoleResult = await _userManager.AddToRoleAsync(user, OwnerRole);
      if (!addRoleResult.Succeeded)
      {
        throw ToIdentityException(addRoleResult);
      }
    }, ct);

    _logger.LogInformation(
      "OwnerRegistered UserId={UserId} HasPromo={HasPromo} Category={Category}",
      user.Id,
      !string.IsNullOrWhiteSpace(request.PromoCode),
      "audit");

    return new RegisterOwnerResult(user.Id, user.Email ?? request.Email);
  }

  private async Task EnsureRoleExistsAsync(string role)
  {
    if (await _roleManager.RoleExistsAsync(role))
    {
      return;
    }

    var result = await _roleManager.CreateAsync(new IdentityRole<Guid>
    {
      Id = Guid.NewGuid(),
      Name = role,
    });
    if (!result.Succeeded)
    {
      throw ToIdentityException(result);
    }
  }

  private static IdentityOperationException ToIdentityException(IdentityResult result)
  {
    var errors = result.Errors
      .GroupBy(error => error.Code)
      .ToDictionary(
        group => group.Key,
        group => group.Select(error => error.Description).ToArray());

    return new IdentityOperationException(errors);
  }
}
