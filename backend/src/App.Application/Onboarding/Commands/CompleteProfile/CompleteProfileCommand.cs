using App.Application.Booking.PromoCodes;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Onboarding.Commands.CompleteProfile;

/// <summary>
/// Krok kreatora „Jak się nazywasz? / Nazwa salonu" — TU powstaje Tenant + Employee (Droga B).
///
/// UWAGA: to JEDYNY handler onboardingu, który NIE dziedziczy po <c>TenantHandler&lt;,&gt;</c> —
/// tenant jeszcze nie istnieje, więc <c>UserId</c> jest przekazywany z claima przez kontroler,
/// a odczyty muszą iść z <c>IgnoreQueryFilters()</c> (brak rozwiązanego tenanta = filtry odcięłyby
/// wszystko). NIE „naprawiać" tego na <c>GetResolvedTenantId()</c>.
/// </summary>
public sealed record CompleteProfileCommand(
  Guid UserId,
  string FirstName,
  string LastName,
  string SalonName,
  string SalonSlug) : IRequest<CompleteProfileResult>;

public sealed record CompleteProfileResult(Guid TenantId, Guid EmployeeId);

public sealed class CompleteProfileCommandValidator : AbstractValidator<CompleteProfileCommand>
{
  public CompleteProfileCommandValidator()
  {
    RuleFor(x => x.UserId).NotEmpty();
    RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
    RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    RuleFor(x => x.SalonName).NotEmpty().MaximumLength(100);
    RuleFor(x => x.SalonSlug).NotEmpty().MaximumLength(100)
      .Matches("^[a-zA-Z0-9-]+$")
      .WithMessage("Adres salonu może zawierać tylko litery, cyfry i myślnik.");
  }
}

internal sealed class CompleteProfileCommandHandler
  : IRequestHandler<CompleteProfileCommand, CompleteProfileResult>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ITenantVatRateSeeder _vatRateSeeder;
  private readonly IPromoCodeRedemptionService _promoCodeRedemption;
  private readonly ILogger<CompleteProfileCommandHandler> _logger;

  public CompleteProfileCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ITenantVatRateSeeder vatRateSeeder,
    IPromoCodeRedemptionService promoCodeRedemption,
    ILogger<CompleteProfileCommandHandler> logger)
  {
    _context = context;
    _uow = uow;
    _vatRateSeeder = vatRateSeeder;
    _promoCodeRedemption = promoCodeRedemption;
    _logger = logger;
  }

  public async Task<CompleteProfileResult> Handle(CompleteProfileCommand request, CancellationToken ct)
  {
    var user = await _context.Users
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
      ?? throw new NotFoundException("User", request.UserId);

    // API broni się samo — front nie jest granicą bezpieczeństwa (mina #4 z planu onboardingu).
    if (!user.EmailConfirmed || !user.PhoneNumberConfirmed)
    {
      throw new OnboardingNotVerifiedException();
    }

    var slug = request.SalonSlug.Trim();

    // Salon już istnieje (powrót w kreatorze albo podwójny submit): nie tworzymy drugiego, ale
    // ZAPISUJEMY to, co przyszło. Wcześniej handler zwracał tu tylko istniejące id i po cichu
    // wyrzucał nadesłaną nazwę/slug/imię — użytkownik wracał na krok 2, poprawiał nazwę salonu,
    // klikał „Dalej", nie dostawał żadnego błędu i szedł dalej z NIEZMIENIONYMI danymi.
    var existing = await _context.Employees
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(e => e.UserId == request.UserId && e.IsActive, ct);
    if (existing != null)
    {
      // Slug wolno zmienić na dowolny NIEZAJĘTY — ale własny slug nie może blokować sam siebie
      // (stąd t.Id != existing.TenantId). Bez tego powrót na krok 2 kończył się 409 na własnym linku.
      var takenByAnother = await _context.Tenants
        .IgnoreQueryFilters()
        .AnyAsync(t => t.Slug == slug && t.Id != existing.TenantId, ct);
      if (takenByAnother)
      {
        throw new SalonSlugTakenException();
      }

      var ownTenant = await _context.Tenants
        .IgnoreQueryFilters()
        .FirstAsync(t => t.Id == existing.TenantId, ct);

      // Update(name, slug) rusza WYŁĄCZNIE te dwa pola — pozostałe parametry są opcjonalne
      // i nieprzekazane, więc ustawienia salonu z dalszych kroków kreatora zostają nietknięte.
      ownTenant.Update(request.SalonName, slug);
      existing.UpdatePersonalData(request.FirstName, request.LastName);
      await _context.SaveChangesAsync(ct);

      _logger.LogInformation(
        "OnboardingProfileUpdated UserId={UserId} TenantId={TenantId} Category=audit",
        request.UserId, existing.TenantId);

      return new CompleteProfileResult(existing.TenantId, existing.Id);
    }

    var slugTaken = await _context.Tenants
      .IgnoreQueryFilters()
      .AnyAsync(t => t.Slug == slug, ct);
    if (slugTaken)
    {
      throw new SalonSlugTakenException();
    }

    var tenant = new Tenant(request.SalonName, slug, "Europe/Warsaw", "PLN");
    tenant.StartTrial();

    var employee = new Employee(
      tenant.Id,
      user.Id,
      request.FirstName,
      request.LastName,
      user.Email ?? string.Empty);

    await _uow.ExecuteInTransactionAsync(async _ =>
    {
      _context.Tenants.Add(tenant);
      _context.Employees.Add(employee);
      _vatRateSeeder.SeedDefaults(tenant.Id, "PL");

      // Ustaw prawdziwą nazwę wyświetlaną (dotąd placeholder = e-mail).
      var displayName = $"{request.FirstName} {request.LastName}".Trim();
      user.UpdateProfile(user.Email ?? string.Empty, displayName);

      // Odroczony kod promo — redempcja atomowa w tej samej transakcji. Niepowodzenie (zły /
      // wygasły / wyczerpany kod) NIE blokuje utworzenia salonu — log + kontynuuj bez rabatu.
      // Kod czyścimy zawsze (po sukcesie i po odrzuceniu), żeby nie próbować go ponownie.
      if (!string.IsNullOrWhiteSpace(user.PendingPromoCode))
      {
        var code = user.PendingPromoCode!;
        var redemption = await _promoCodeRedemption.RedeemForNewTenantAsync(code, tenant, ct);
        if (redemption is null)
        {
          _logger.LogInformation(
            "PromoCode redemption skipped during complete-profile (TenantId={TenantId}).",
            tenant.Id);
        }
        user.ClearPendingPromoCode();
      }

      await _context.SaveChangesAsync(ct);
    }, ct);

    _logger.LogInformation(
      "OnboardingProfileCompleted UserId={UserId} TenantId={TenantId} EmployeeId={EmployeeId} Category={Category}",
      user.Id, tenant.Id, employee.Id, "audit");

    return new CompleteProfileResult(tenant.Id, employee.Id);
  }
}
