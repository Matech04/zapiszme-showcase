using App.Application.Appointments.Commands.ApplyReschedule;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Booking.SelfService.Commands;

public record RescheduleSelfServiceAppointmentCommand(
  string Slug,
  Guid SessionToken,
  Guid AppointmentId,
  Guid EmployeeId,
  Guid ServiceId,
  DateOnly Date,
  TimeOnly StartTime) : IRequest;

internal sealed class RescheduleSelfServiceAppointmentCommandHandler
    : IRequestHandler<RescheduleSelfServiceAppointmentCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ISender _mediator;
  private readonly IPublisher _publisher;
  private readonly ILogger<RescheduleSelfServiceAppointmentCommandHandler> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly IBookingOtpProtection _otpProtection;
  private readonly IHttpContextAccessor _httpContextAccessor;

  public RescheduleSelfServiceAppointmentCommandHandler(
    IApplicationDbContext context,
    ISender mediator,
    IPublisher publisher,
    ILogger<RescheduleSelfServiceAppointmentCommandHandler> logger,
    TimeProvider timeProvider,
    IBookingOtpProtection otpProtection,
    IHttpContextAccessor httpContextAccessor)
  {
    _context = context;
    _mediator = mediator;
    _publisher = publisher;
    _logger = logger;
    _timeProvider = timeProvider;
    _otpProtection = otpProtection;
    _httpContextAccessor = httpContextAccessor;
  }

  public async Task Handle(RescheduleSelfServiceAppointmentCommand request, CancellationToken ct)
  {
    var session = await SelfServiceSessionResolver.ResolveAsync(_context, request.Slug, request.SessionToken, _timeProvider, ct);

    // INWARIANT (self-service, izolacja + anti-IDOR): przy IgnoreQueryFilters OBA warunki są
    // obowiązkowe — (1) filtr `a.TenantId == session.TenantId` (izolacja tenanta) ORAZ
    // (2) dopasowanie kontaktu klienta do sesji poniżej (matchesEmail/matchesPhone). Nie usuwaj żadnego.
    var appointment = await _context.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(a => a.Id == request.AppointmentId && a.TenantId == session.TenantId, ct)
      ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    if (appointment.CustomerId is null)
    {
      throw new ForbiddenAccessException("Nie masz dostępu do tej wizyty.", ErrorCodes.AppointmentOtpInvalidLease);
    }

    var customer = await _context.Customers
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(c => c.Id == appointment.CustomerId, ct)
      ?? throw new ForbiddenAccessException("Nie masz dostępu do tej wizyty.", ErrorCodes.AppointmentOtpInvalidLease);

    var matchesEmail = session.Email != null
      && !string.IsNullOrEmpty(customer.Email)
      && string.Equals(customer.Email, session.Email, StringComparison.OrdinalIgnoreCase);
    var matchesPhone = session.PhoneE164 != null
      && customer.PhoneNumber != null
      && string.Equals(customer.PhoneNumber.Value, session.PhoneE164, StringComparison.Ordinal);

    if (!matchesEmail && !matchesPhone)
    {
      throw new ForbiddenAccessException("Nie masz dostępu do tej wizyty.", ErrorCodes.AppointmentOtpInvalidLease);
    }

    // Zapisujemy stary termin przed zmianą
    var oldDate = appointment.Date;
    var oldStartTime = appointment.StartTime;

    // No-op: klient wysłał ten sam termin (np. podwójny submit) — nie przekładamy i NIE publikujemy
    // powiadomienia (brak płatnego SMS „zmieniono termin" za brak zmiany).
    if (oldDate == request.Date && oldStartTime == request.StartTime)
    {
      return;
    }

    // Cap anti-abuse: każde przełożenie wyzwala płatny SMS. Bez tego jedna ważna sesja mogłaby w pętli
    // reschedule (A→B→A→…) drenować budżet SMS salonu — endpoint jest anonimowy, tylko rate-limitowany
    // per-IP. Wołane PO walidacji sesji/dostępu i PO odrzuceniu no-op, PRZED samym przełożeniem.
    var clientIp = App.Application.Booking.BookingClientIp.From(_httpContextAccessor.HttpContext);
    _otpProtection.AssertCanReschedule(request.SessionToken, appointment.Id, clientIp);

    // Self-service NIE zmienia składu combo — reużywamy istniejących pozycji wizyty (kolejność wg Position).
    // request.ServiceId zachowane w kontrakcie dla zgodności, ale celowo ignorowane.
    var serviceIds = await _context.Appointments
      .IgnoreQueryFilters()
      .Where(a => a.Id == request.AppointmentId && a.TenantId == session.TenantId)
      .SelectMany(a => a.Items)
      .OrderBy(i => i.Position)
      .Select(i => i.ServiceId)
      .ToListAsync(ct);

    await _mediator.Send(
      new ApplyRescheduleCommand(
        appointment.Id,
        request.EmployeeId,
        serviceIds,
        request.Date,
        request.StartTime,
        IsSelfService: true,
        // Klient przekłada własną wizytę — obowiązuje horyzont i publikacja miesiąca.
        EnforcePublicVisibility: true),
      ct);

    // Oznaczamy wizytę jako zmienioną przez klienta (best-effort — nie cofa przełożenia)
    try
    {
      var tracked = await _context.Appointments
        .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct);
      if (tracked is not null)
      {
        tracked.RecordSelfServiceChange(2);
        await _context.SaveChangesAsync(ct);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Nie udało się oznaczyć wizyty {AppointmentId} jako przesuniętej przez klienta", request.AppointmentId);
    }

    // Powiadomienia best-effort — błąd publikacji nie cofa przełożenia.
    try
    {
      await _publisher.Publish(
        new AppointmentRescheduledEvent(appointment.TenantId, appointment.Id, oldDate, oldStartTime),
        ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja AppointmentRescheduledEvent dla wizyty {AppointmentId} nie powiodła się", appointment.Id);
    }
  }
}
