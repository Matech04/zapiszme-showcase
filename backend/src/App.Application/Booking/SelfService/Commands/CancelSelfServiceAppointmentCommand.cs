using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Booking.SelfService.Commands;

public record CancelSelfServiceAppointmentCommand(string Slug, Guid SessionToken, Guid AppointmentId) : IRequest;

internal sealed class CancelSelfServiceAppointmentCommandHandler
    : IRequestHandler<CancelSelfServiceAppointmentCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly IPublisher _publisher;
  private readonly ILogger<CancelSelfServiceAppointmentCommandHandler> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly IAppointmentInspirationCleanup _inspirationCleanup;

  public CancelSelfServiceAppointmentCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    IPublisher publisher,
    ILogger<CancelSelfServiceAppointmentCommandHandler> logger,
    TimeProvider timeProvider,
    IAppointmentInspirationCleanup inspirationCleanup)
  {
    _context = context;
    _uow = uow;
    _publisher = publisher;
    _logger = logger;
    _timeProvider = timeProvider;
    _inspirationCleanup = inspirationCleanup;
  }

  public async Task Handle(CancelSelfServiceAppointmentCommand request, CancellationToken ct)
  {
    var session = await SelfServiceSessionResolver.ResolveAsync(_context, request.Slug, request.SessionToken, _timeProvider, ct);

    // INWARIANT (self-service, izolacja + anti-IDOR): przy IgnoreQueryFilters OBA warunki są
    // obowiązkowe — (1) filtr `a.TenantId == session.TenantId` (izolacja tenanta) ORAZ
    // (2) BelongsToSessionAsync poniżej (kontakt klienta == kontakt sesji). Nie usuwaj żadnego.
    var appointment = await _context.Appointments
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(a => a.Id == request.AppointmentId && a.TenantId == session.TenantId, ct)
      ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    if (!await BelongsToSessionAsync(appointment, session, ct))
    {
      throw new ForbiddenAccessException("Nie masz dostępu do tej wizyty.", ErrorCodes.AppointmentOtpInvalidLease);
    }

    var todayUtc = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
    if (appointment.Date < todayUtc)
    {
      throw new AppointmentBookingRuleException(
        "Nie można anulować wizyty z przeszłości.",
        ErrorCodes.InvalidArgument);
    }

    appointment.RecordSelfServiceChange(1);
    appointment.ChangeStatus(AppointmentStatus.Canceled);

    // Anulowana wizyta — podgląd inspiracji zbędny: zdejmij rekordy i po commit skasuj obiekty z R2.
    var inspirationKeys = _inspirationCleanup.DetachImages(appointment);

    await _uow.SaveChangesAsync(ct);

    await _inspirationCleanup.PurgeStorageAsync(inspirationKeys, ct);

    // Powiadomienia best-effort — błąd publikacji nie cofa anulowania.
    try
    {
      await _publisher.Publish(
        new AppointmentCancelledEvent(appointment.TenantId, appointment.Id),
        ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja AppointmentCancelledEvent dla wizyty {AppointmentId} nie powiodła się", appointment.Id);
    }
  }

  private async Task<bool> BelongsToSessionAsync(Appointment appointment, SelfServiceSession session, CancellationToken ct)
  {
    if (appointment.CustomerId is null) return false;
    var customer = await _context.Customers
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(c => c.Id == appointment.CustomerId, ct);
    if (customer is null) return false;

    if (session.Email != null)
    {
      return !string.IsNullOrEmpty(customer.Email)
        && string.Equals(customer.Email, session.Email, StringComparison.OrdinalIgnoreCase);
    }

    if (session.PhoneE164 != null)
    {
      return customer.PhoneNumber != null
        && string.Equals(customer.PhoneNumber.Value, session.PhoneE164, StringComparison.Ordinal);
    }

    return false;
  }
}
