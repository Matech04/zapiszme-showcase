using App.Application.Common.Interfaces;
using App.Application.Payments.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Payments.Commands.HandlePaymentWebhook;

/// <summary>
/// Obsługuje webhook operatora płatności. Bez kontekstu tenanta (zdarzenie przychodzi od operatora,
/// nie od zalogowanego salonu) — appointment ładowany z <c>IgnoreQueryFilters</c>; write-check
/// pomijany gdy brak bieżącego tenanta. Idempotentne po sesji płatności (Stripe ponawia dostarczanie).
/// </summary>
public record HandlePaymentWebhookCommand(string Provider, string Payload, string Signature) : IRequest;

internal class HandlePaymentWebhookCommandHandler : IRequestHandler<HandlePaymentWebhookCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IPaymentProvider _payments;
  private readonly ILogger<HandlePaymentWebhookCommandHandler> _logger;

  public HandlePaymentWebhookCommandHandler(
    IApplicationDbContext context,
    IPaymentProvider payments,
    ILogger<HandlePaymentWebhookCommandHandler> logger)
  {
    _context = context;
    _payments = payments;
    _logger = logger;
  }

  public async Task Handle(HandlePaymentWebhookCommand request, CancellationToken ct)
  {
    var evt = _payments.ParseAndVerifyWebhook(request.Payload, request.Signature);

    if (evt.Type != PaymentWebhookEventType.PaymentSucceeded || !evt.AppointmentId.HasValue)
    {
      _logger.LogInformation("Webhook płatności {Type} (sesja {SessionId}) — pominięty.", evt.Type, evt.SessionId);
      return;
    }

    var appointment = await _context.Appointments
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(a => a.Id == evt.AppointmentId.Value, ct);

    if (appointment is null)
    {
      _logger.LogWarning("Webhook płatności dla nieznanej wizyty {AppointmentId}.", evt.AppointmentId);
      return;
    }

    // Defense-in-depth (mutacja przez IgnoreQueryFilters omija izolację tenanta): potwierdź, że
    // tenant z podpisanych metadanych zgadza się z wizytą.
    if (evt.TenantId.HasValue && appointment.TenantId != evt.TenantId.Value)
    {
      _logger.LogWarning(
        "Webhook dla wizyty {AppointmentId}: tenant z metadanych nie zgadza się z tenantem wizyty — pominięty.",
        appointment.Id);
      return;
    }

    // Potwierdź, że konto połączone ze zdarzenia (Stripe-Account) należy do salonu tej wizyty.
    if (!string.IsNullOrEmpty(evt.ConnectedAccountId))
    {
      var expectedAccount = await _context.Tenants
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(t => t.Id == appointment.TenantId)
        .Select(t => t.MerchantAccount != null ? t.MerchantAccount.AccountId : null)
        .FirstOrDefaultAsync(ct);

      if (!string.IsNullOrEmpty(expectedAccount) && expectedAccount != evt.ConnectedAccountId)
      {
        _logger.LogWarning(
          "Webhook dla wizyty {AppointmentId}: konto połączone nie należy do salonu wizyty — pominięty.",
          appointment.Id);
        return;
      }
    }

    // Zabezpieczenie przed pomyłką sesji: ignoruj jeśli identyfikator sesji nie pasuje (np. po regeneracji).
    if (!string.IsNullOrEmpty(evt.SessionId)
        && !string.IsNullOrEmpty(appointment.PaymentSessionId)
        && appointment.PaymentSessionId != evt.SessionId)
    {
      _logger.LogInformation(
        "Webhook dla wizyty {AppointmentId} dotyczy nieaktualnej sesji {SessionId} — pominięty.",
        appointment.Id, evt.SessionId);
      return;
    }

    appointment.MarkDepositPaid(DateTime.UtcNow);
    await _context.SaveChangesAsync(ct);

    _logger.LogInformation("Zadatek opłacony dla wizyty {AppointmentId} (sesja {SessionId}).",
      appointment.Id, evt.SessionId);
  }
}
