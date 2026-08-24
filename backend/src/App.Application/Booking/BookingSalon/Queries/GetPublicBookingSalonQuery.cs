using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingSalon.Queries;

public record PublicBookingSalonInfoDto(
  string Name,
  string Slug,
  int CustomerVerificationChannel,
  int BookingAccessPolicy,
  bool IsBookingAvailable,
  bool RequireCustomerName,
  bool CollectInstagramHandle,
  bool CollectInspirationImages,
  bool IsBookingPaused = false,
  string? BookingPauseMessage = null,
  string? BookingCalendarColorHex = null,
  string? BookingCalendarBackgroundHex = null,
  string? BookingCalendarSurfaceHex = null,
  string? BookingCalendarPriceHex = null,
  string? TermsOfService = null,
  bool IsPlatformMaintenance = false,
  string? PlatformMaintenanceMessage = null,
  // Ile dni naprzód klient może przeglądać kalendarz. Wcześniej stała MAX_MONTHS_AHEAD = 3
  // zduplikowana w dwóch komponentach Svelte i w backendzie, spięta wyłącznie komentarzem.
  int BookingHorizonDays = 120);

public record GetPublicBookingSalonQuery(string Slug) : IRequest<PublicBookingSalonInfoDto>;

public sealed class GetPublicBookingSalonQueryHandler
    : IRequestHandler<GetPublicBookingSalonQuery, PublicBookingSalonInfoDto>
{
  private readonly IApplicationDbContext _context;
  private readonly IPlatformMaintenanceState _maintenanceState;

  public GetPublicBookingSalonQueryHandler(
    IApplicationDbContext context,
    IPlatformMaintenanceState maintenanceState)
  {
    _context = context;
    _maintenanceState = maintenanceState;
  }

  public async Task<PublicBookingSalonInfoDto> Handle(
    GetPublicBookingSalonQuery request,
    CancellationToken cancellationToken)
  {
    var tenant = await _context.Tenants
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Slug == request.Slug, cancellationToken)
        ?? throw new NotFoundException("Tenant", request.Slug);

    // Globalny tryb serwisowy platformy ma priorytet — web pokaże ekran „prace serwisowe"
    // zamiast pozwalać klientowi wejść w flow (write i tak zablokuje middleware 503).
    var maintenance = await _maintenanceState.GetAsync(cancellationToken);

    // Publiczna rezerwacja dostępna gdy subskrypcja aktywna (Trial w trakcie lub Active)
    // ORAZ salon nie wstrzymał rezerwacji. PastDue / Canceled / wygasły Trial / wstrzymane rezerwacje
    // → blokujemy publiczne booking flow. IsBookingPaused wystawiamy osobno, żeby web pokazał
    // dedykowany komunikat „rezerwacje wstrzymane", a nie generyczny ekran wyczerpanego limitu.
    var effectiveStatus = tenant.Subscription.EffectiveStatus;
    var subscriptionActive = effectiveStatus == SubscriptionStatus.Trial
                          || effectiveStatus == SubscriptionStatus.Active;
    var isBookingAvailable = subscriptionActive && !tenant.BookingPaused;

    return new PublicBookingSalonInfoDto(
        tenant.Name,
        tenant.Slug,
        (int)tenant.CustomerVerificationChannel,
        (int)tenant.BookingAccessPolicy,
        isBookingAvailable,
        tenant.RequireCustomerName,
        tenant.CollectInstagramHandle,
        tenant.CollectInspirationImages,
        tenant.BookingPaused,
        tenant.BookingPauseMessage,
        tenant.BookingCalendarColorHex,
        tenant.BookingCalendarBackgroundHex,
        tenant.BookingCalendarSurfaceHex,
        tenant.BookingCalendarPriceHex,
        tenant.TermsOfService,
        maintenance.Enabled,
        maintenance.Message,
        tenant.BookingHorizonDays);
  }
}
