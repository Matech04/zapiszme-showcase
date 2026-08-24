using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.BookingSalon.Queries;

/// <summary>
/// Rozwiązuje white-label host (np. <c>rezerwacja.salon-przyklad.pl</c>) na publiczne info salonu
/// (w tym <c>Slug</c>), żeby SPA bookingu serwowane pod customową domeną wiedziało, którego tenanta
/// otworzyć — tam nie ma slugu w ścieżce. Najpierw tani pre-check w rejestrze (zero I/O dla nieznanych
/// hostów → ochrona bazy przed enumeracją/DoS), potem JEDNO zapytanie po indeksowanym <c>CustomDomain</c>.
/// </summary>
public record ResolveBookingHostQuery(string Host) : IRequest<PublicBookingSalonInfoDto>;

public sealed class ResolveBookingHostQueryHandler
    : IRequestHandler<ResolveBookingHostQuery, PublicBookingSalonInfoDto>
{
  private readonly IApplicationDbContext _context;
  private readonly ICustomDomainRegistry _registry;

  public ResolveBookingHostQueryHandler(IApplicationDbContext context, ICustomDomainRegistry registry)
  {
    _context = context;
    _registry = registry;
  }

  public async Task<PublicBookingSalonInfoDto> Handle(
    ResolveBookingHostQuery request,
    CancellationToken cancellationToken)
  {
    // Pre-check w pamięci: nieznany host → 404 bez dotykania bazy (anty-enumeracja + anty-DoS).
    if (!_registry.IsManagedHost(request.Host))
    {
      throw new NotFoundException("BookingHost", request.Host ?? "(null)");
    }

    var baseDomain = CustomDomainHost.ToBaseDomain(request.Host)
        ?? throw new NotFoundException("BookingHost", request.Host);

    var tenant = await _context.Tenants
        .AsNoTracking()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(t => t.CustomDomain == baseDomain, cancellationToken)
        ?? throw new NotFoundException("BookingHost", request.Host);

    // Bezpieczeństwo (anty-enumeracja statusu rozliczeń): nieaktywny salon (PastDue/Canceled/wygasły
    // Trial) traktujemy jak nieznany host → 404, zamiast ujawniać anonimowi IsBookingAvailable=false.
    // Inaczej resolve-host pozwalałby zmapować, który klient premium ma zaległości płatnicze.
    var effectiveStatus = tenant.Subscription.EffectiveStatus;
    if (effectiveStatus != SubscriptionStatus.Trial && effectiveStatus != SubscriptionStatus.Active)
    {
      throw new NotFoundException("BookingHost", request.Host);
    }

    return new PublicBookingSalonInfoDto(
        tenant.Name,
        tenant.Slug,
        (int)tenant.CustomerVerificationChannel,
        (int)tenant.BookingAccessPolicy,
        IsBookingAvailable: true,
        tenant.RequireCustomerName,
        tenant.CollectInstagramHandle,
        tenant.CollectInspirationImages);
  }
}
