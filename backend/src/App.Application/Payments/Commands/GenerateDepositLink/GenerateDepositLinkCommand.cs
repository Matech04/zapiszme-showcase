using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Payments.Abstractions;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.ShortLinkAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace App.Application.Payments.Commands.GenerateDepositLink;

/// <summary>
/// Generuje link płatności zadatku dla istniejącej (zarezerwowanej) wizyty. Kwota prefillowana
/// z ustawień salonu, ale personel może ją nadpisać (<paramref name="AmountOverride"/>). Środki idą
/// bezpośrednio na konto salonu. Link ważny 24h; regeneracja nadpisuje poprzedni nieopłacony.
/// </summary>
public record GenerateDepositLinkCommand(Guid AppointmentId, decimal? AmountOverride = null)
  : IRequest<GenerateDepositLinkResult>;

public record GenerateDepositLinkResult(string Url, DateTime ExpiresAtUtc, decimal Amount, string Currency);

internal class GenerateDepositLinkCommandHandler
    : TenantHandler<GenerateDepositLinkCommand, GenerateDepositLinkResult>
{
  private readonly IApplicationDbContext _context;
  private readonly IStaffAccessPolicy _access;
  private readonly IPaymentProvider _payments;
  private readonly IConfiguration _config;

  public GenerateDepositLinkCommandHandler(
    ICurrentTenantService currentTenantService,
    IStaffAccessPolicy access,
    IApplicationDbContext context,
    IPaymentProvider payments,
    IConfiguration config)
    : base(currentTenantService)
  {
    _context = context;
    _access = access;
    _payments = payments;
    _config = config;
  }

  public override async Task<GenerateDepositLinkResult> Handle(GenerateDepositLinkCommand request, CancellationToken ct)
  {
    // Zadatek to operacja na cudzym kalendarzu — ta sama bramka co przy zmianie wizyty.
    // Kolejność jak dawniej w kontrolerze: 403 przed regułami biznesowymi zadatku.
    await _access.EnsureCanMutateAppointmentAsync(request.AppointmentId, ct);

    var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    if (!tenant.DepositSettings.Enabled)
    {
      throw new AppointmentBookingRuleException(
        "Zadatki są wyłączone w ustawieniach salonu.", ErrorCodes.DepositNotEnabled);
    }

    if (tenant.MerchantAccount is null)
    {
      throw new AppointmentBookingRuleException(
        "Konto płatności nie jest połączone.", ErrorCodes.MerchantAccountNotConnected);
    }

    if (!tenant.CanAcceptDeposits)
    {
      throw new AppointmentBookingRuleException(
        "Konto płatności nie jest jeszcze gotowe do przyjmowania wpłat.", ErrorCodes.MerchantAccountNotReady);
    }

    var appointment = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct)
      ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    Money amount = request.AmountOverride.HasValue
      ? new Money(Math.Round(request.AmountOverride.Value, 2, MidpointRounding.AwayFromZero), appointment.TotalPrice.Currency)
      : tenant.DepositSettings.ComputeDefault(appointment.TotalPrice);

    if (amount.Amount <= 0)
    {
      throw new AppointmentBookingRuleException(
        "Kwota zadatku musi być większa od zera.", ErrorCodes.InvalidArgument);
    }

    if (amount.Amount > appointment.TotalPrice.Amount)
    {
      throw new AppointmentBookingRuleException(
        "Kwota zadatku nie może przekraczać ceny wizyty.", ErrorCodes.DepositAmountExceedsTotal);
    }

    var service = await _context.Services.FirstOrDefaultAsync(s => s.Id == appointment.ServiceId, ct);
    var instrumentLabel = InstrumentLabel(tenant.DepositSettings.Instrument);
    var description = service is null ? instrumentLabel : $"{instrumentLabel} — {service.Name}";

    // White-label: dla salonu z customową domeną link i powrót lądują na rezerwacja.<domena>,
    // inaczej na globalnej domenie platformy (config).
    var (shortBase, webBaseUrl) = App.Application.Payments.DepositUrls.Resolve(
      tenant.CustomDomain, _config["Payments:WebBaseUrl"], _config["ShortLink:BaseUrl"]);
    var successUrl = $"{webBaseUrl}/platnosc/sukces?wizyta={appointment.Id}";
    var cancelUrl = $"{webBaseUrl}/platnosc/anulowano?wizyta={appointment.Id}";

    var session = await _payments.CreatePaymentSessionAsync(
      new PaymentSessionRequest(
        amount,
        appointment.Id,
        TenantId,
        tenant.MerchantAccount.AccountId,
        instrumentLabel,
        description,
        successUrl,
        cancelUrl),
      ct);

    // Skrócony link na naszej domenie (zapisz.me/p/{code}) → redirect na Stripe Checkout.
    // Klientce wysyłamy krótki, „nasz" link zamiast długiego URL-a Stripe.
    var code = await GenerateUniqueShortCodeAsync(ct);
    _context.ShortLinks.Add(new ShortLink(
      code, session.Url, "deposit", TenantId, appointment.Id, DateTime.UtcNow, session.ExpiresAtUtc));

    var shortUrl = $"{shortBase}/p/{code}";

    appointment.GenerateDepositLink(amount, session.SessionId, shortUrl, session.ExpiresAtUtc, DateTime.UtcNow);
    await _context.SaveChangesAsync(ct);

    return new GenerateDepositLinkResult(shortUrl, session.ExpiresAtUtc, amount.Amount, amount.Currency);
  }

  private async Task<string> GenerateUniqueShortCodeAsync(CancellationToken ct)
  {
    for (var attempt = 0; attempt < 5; attempt++)
    {
      var code = ShortCodeGenerator.Generate();
      var exists = await _context.ShortLinks.AnyAsync(l => l.Code == code, ct);
      if (!exists)
      {
        return code;
      }
    }

    // Skrajnie nieprawdopodobne przy 7 znakach base62 — fallback do dłuższego kodu.
    return ShortCodeGenerator.Generate(12);
  }

  private static string InstrumentLabel(DepositInstrument instrument) => instrument switch
  {
    DepositInstrument.Zadatek => "Zadatek",
    DepositInstrument.Zaliczka => "Zaliczka",
    DepositInstrument.OplataRezerwacyjna => "Opłata rezerwacyjna",
    _ => "Zadatek",
  };
}
