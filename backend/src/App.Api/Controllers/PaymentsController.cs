using App.Application.Payments.Commands.HandlePaymentWebhook;
using App.Application.Payments.Queries.GetPaymentStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

/// <summary>
/// Publiczne endpointy płatności: webhook operatora (potwierdzenie zapłaty) oraz odczyt statusu
/// płatności wizyty dla strony powrotu. Bez auth i bez kontekstu tenanta.
/// </summary>
[AllowAnonymous]
public class PaymentsController : ApiControllerBase
{
  /// <summary>
  /// Webhook operatora. Czyta surowe body + nagłówek podpisu; weryfikacja podpisu w handlerze.
  /// Hojny limiter per-IP (polityka „Webhook") zamiast pełnego wyłączenia — chroni przed
  /// anonimowym floodem (HMAC po body do 1 MB), a próg jest wysoko ponad realny retry Stripe.
  /// </summary>
  [HttpPost("webhook/{provider}")]
  [EnableRateLimiting("Webhook")]
  public async Task<IActionResult> Webhook([FromRoute] string provider, CancellationToken ct)
  {
    using var reader = new StreamReader(Request.Body);
    var payload = await reader.ReadToEndAsync(ct);
    var signature = Request.Headers["Stripe-Signature"].ToString();

    try
    {
      await Mediator.Send(new HandlePaymentWebhookCommand(provider, payload, signature), ct);
      return Ok();
    }
    catch (Exception)
    {
      // Nieprawidłowy podpis / błąd przetwarzania → 400. Operator ponowi dostarczenie.
      return BadRequest();
    }
  }

  /// <summary>Status płatności wizyty (po GUID) — polling strony powrotu po redirectcie z Checkout.</summary>
  [HttpGet("status/{appointmentId:guid}")]
  public async Task<ActionResult<PaymentStatusDto>> Status([FromRoute] Guid appointmentId, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetPaymentStatusQuery(appointmentId), ct);
    return Ok(result);
  }
}
