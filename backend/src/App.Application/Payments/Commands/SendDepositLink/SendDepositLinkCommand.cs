using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Payments.Commands.SendDepositLink;

/// <summary>
/// Wysyła do klienta istniejący link zadatku wybranym kanałem (SMS albo e-mail). Domyślnie kanał
/// salonu (<see cref="Tenant.CustomerVerificationChannel"/>), z opcją nadpisania. Reuse pełnego
/// pipeline'u powiadomień — automatyczny guard kosztów SMS, normalizacja telefonu, transliteracja,
/// pomijanie demo. Akcja jawna personelu, więc pomijamy bramkę NotificationSettings.
/// </summary>
public record SendDepositLinkCommand(Guid AppointmentId, CustomerVerificationChannel? Channel = null)
  : IRequest<SendDepositLinkResult>;

public record SendDepositLinkResult(string Channel);

internal class SendDepositLinkCommandHandler
  : TenantHandler<SendDepositLinkCommand, SendDepositLinkResult>
{
  /// <summary>
  /// Okno, w którym ponowna wysyłka tego samego linku jest odrzucana. Każda wysyłka to płatny SMS,
  /// a przycisk „Wyślij ponownie" kusi do wielokrotnego kliknięcia — bez tej bramki klient dostaje
  /// serię identycznych wiadomości na koszt salonu.
  /// </summary>
  private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(3);

  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;
  private readonly INotificationDispatcher _dispatcher;
  private readonly ISmsUsageGuard _smsUsageGuard;
  private readonly ILogger<SendDepositLinkCommandHandler> _logger;

  public SendDepositLinkCommandHandler(
    ICurrentTenantService currentTenantService,
    IStaffAccessPolicy access,
    IApplicationDbContext context,
    INotificationDispatcher dispatcher,
    ISmsUsageGuard smsUsageGuard,
    ILogger<SendDepositLinkCommandHandler> logger)
    : base(currentTenantService)
  {
    _context = context;
    _access = access;
    _dispatcher = dispatcher;
    _smsUsageGuard = smsUsageGuard;
    _logger = logger;
  }

  public override async Task<SendDepositLinkResult> Handle(SendDepositLinkCommand request, CancellationToken ct)
  {
    // Zadatek to operacja na cudzym kalendarzu — ta sama bramka co przy zmianie wizyty.
    // Kolejność jak dawniej w kontrolerze: 403 przed regułami biznesowymi zadatku.
    await _access.EnsureCanMutateAppointmentAsync(request.AppointmentId, ct);

    var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct)
      ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    if (appointment.PaymentStatus == AppointmentPaymentStatus.Paid)
    {
      throw new AppointmentBookingRuleException(
        "Zadatek za tę wizytę został już opłacony.", ErrorCodes.DepositAlreadyPaid);
    }

    if (appointment.PaymentStatus != AppointmentPaymentStatus.AwaitingPayment
        || string.IsNullOrWhiteSpace(appointment.PaymentLinkUrl))
    {
      throw new AppointmentBookingRuleException(
        "Najpierw wygeneruj link do zadatku.", ErrorCodes.DepositLinkNotGenerated);
    }

    if (appointment.DepositLinkSentAtUtc is { } sentAt
        && DateTime.UtcNow - sentAt < ResendCooldown)
    {
      throw new AppointmentBookingRuleException(
        "Link został właśnie wysłany. Odczekaj kilka minut przed ponowną wysyłką.",
        ErrorCodes.DepositSendCooldown);
    }

    Customer? customer = appointment.CustomerId is { } customerId
      ? await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
      : null;

    var channel = request.Channel ?? tenant.CustomerVerificationChannel;

    NotificationRecipient recipient;
    if (channel == CustomerVerificationChannel.Phone)
    {
      var phone = customer?.PhoneNumber?.Value;
      if (string.IsNullOrWhiteSpace(phone))
      {
        throw new AppointmentBookingRuleException(
          "Klient nie ma zapisanego numeru telefonu.", ErrorCodes.DepositCustomerContactMissing);
      }

      if (!await _smsUsageGuard.IsWithinMonthlyCapAsync(TenantId, ct))
      {
        throw new AppointmentBookingRuleException(
          "Miesięczny limit SMS został osiągnięty — wyślij link e-mailem lub skopiuj go ręcznie.",
          ErrorCodes.DepositSmsCapReached);
      }

      recipient = new NotificationRecipient(null, phone, null, CustomerDisplayName(customer));
    }
    else
    {
      var email = customer?.Email;
      if (string.IsNullOrWhiteSpace(email))
      {
        throw new AppointmentBookingRuleException(
          "Klient nie ma zapisanego adresu e-mail.", ErrorCodes.DepositCustomerContactMissing);
      }

      recipient = new NotificationRecipient(email, null, null, CustomerDisplayName(customer));
    }

    var amountText = FormatAmount(appointment.DepositAmount);
    var serviceName = await _context.Services
      .Where(s => s.Id == appointment.ServiceId)
      .Select(s => s.Name)
      .FirstOrDefaultAsync(ct);

    var payload = new NotificationPayload(
      SalonName: tenant.Name,
      CustomerName: customer?.FirstName,
      ServiceName: serviceName,
      Date: appointment.Date,
      StartTime: appointment.StartTime,
      ActionUrl: appointment.PaymentLinkUrl,
      AmountText: amountText);

    var dispatch = await _dispatcher.DispatchAsync(new NotificationMessage(
      TenantId,
      NotificationType.DepositLinkToCustomer,
      recipient,
      $"Zadatek za wizytę — {tenant.Name}",
      $"Link do zapłaty zadatku: {appointment.PaymentLinkUrl}",
      payload,
      appointment.Id), ct);

    // Dispatcher jest best-effort i połyka awarie kanałów — dla powiadomień tła tak ma być, ale tu
    // wysyłka JEST operacją. Bez tej kontroli personel widzi „Wysłano", choć smsapi/SMTP odrzuciło
    // wiadomość, a klient nigdy nie dostaje linku do zapłaty.
    var expected = channel == CustomerVerificationChannel.Phone
      ? NotificationChannelKind.Sms
      : NotificationChannelKind.Email;

    if (!dispatch.Delivered(expected))
    {
      throw new AppointmentBookingRuleException(
        "Nie udało się wysłać linku do zadatku. Spróbuj ponownie lub skopiuj link i wyślij ręcznie.",
        ErrorCodes.DepositSendFailed);
    }

    // Od tego miejsca wiadomość JUŻ poszła do klienta i jest opłacona. Pad zapisu stempla nie może
    // zamienić się w „nie wysłano" — personel ponowiłby akcję i zapłacił za drugiego SMS-a, a klient
    // dostałby dwa linki. Zapis jest best-effort: gubimy znacznik w panelu, nie pieniądze.
    try
    {
      appointment.MarkDepositLinkSent(DateTime.UtcNow, expected.ToString());
      await _context.SaveChangesAsync(ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(
        ex,
        "Link do zadatku wysłany kanałem {Channel} dla wizyty {AppointmentId}, ale zapis znacznika padł. "
        + "Panel pokaże wizytę jako niewysłaną.",
        expected,
        appointment.Id);
    }

    return new SendDepositLinkResult(channel.ToString());
  }

  private static string CustomerDisplayName(Customer? customer) =>
    customer is null ? "Klient" : $"{customer.FirstName} {customer.LastName}".Trim();

  private static string? FormatAmount(Money? amount)
  {
    if (amount is null)
    {
      return null;
    }

    var unit = string.Equals(amount.Currency, "PLN", StringComparison.OrdinalIgnoreCase) ? "zł" : amount.Currency;
    return $"{amount.Amount:0.##} {unit}";
  }
}
