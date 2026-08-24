using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers;

/// <summary>
/// Trwałe usunięcie danych osobowych klienta (RODO art. 17). Wspólny rdzeń dla dwóch komend:
/// <c>DeleteCustomerCommand</c> („Usuń klienta” w CRM) i <c>AnonymizeCustomerCommand</c>
/// (jawne żądanie usunięcia danych). Obie robią to samo — soft-delete zostawiał PII w bazie,
/// mimo że deaktywowany klient jest niewidoczny, niedopasowywalny przy rezerwacji online
/// (<c>CustomerRepository.GetByPhoneNumber/GetByEmail</c> respektują query filter) i nieprzywracalny
/// (brak <c>Activate()</c> i endpointu restore). Te dane nie miały już żadnego konsumenta.
///
/// Historia wizyt (FK, kwoty, statusy) zostaje jako dane księgowe.
/// </summary>
internal static class CustomerErasure
{
  /// <summary>
  /// Anonimizuje klienta i czyści powiązane z nim dane osobowe przy wizytach.
  /// NIE woła <c>SaveChangesAsync</c> — robi to wołający, w swojej jednostce pracy.
  /// </summary>
  /// <exception cref="NotFoundException">Klient nie istnieje albo należy do innego tenanta.</exception>
  public static async Task EraseAsync(
    IApplicationDbContext context,
    Guid customerId,
    Guid tenantId,
    CancellationToken ct)
  {
    // IgnoreQueryFilters — łapiemy też wcześniej zdeaktywowanych; tenant ograniczamy ręcznie.
    // Cross-tenant kończy się NotFound (a nie TenantViolation), żeby nie zdradzać istnienia encji.
    var customer = await context.Customers
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(c => c.Id == customerId && c.TenantId == tenantId, ct)
      ?? throw new NotFoundException(nameof(Customer), customerId);

    customer.Anonymize();

    // RODO art. 17 — usuń też dane płatności powiązane z wizytami klienta: identyfikatory linku
    // (URL + sesja) na wizytach oraz skrócone linki (kod → URL Stripe). Kwotę/status zadatku
    // zostawiamy jako dane księgowe. ShortLink nie ma query-filtra tenanta — kasujemy po AppointmentId.
    var appointments = await context.Appointments
      .IgnoreQueryFilters()
      .Where(a => a.CustomerId == customer.Id && a.TenantId == tenantId)
      .ToListAsync(ct);

    if (appointments.Count == 0)
    {
      return;
    }

    foreach (var appointment in appointments)
    {
      appointment.ScrubPaymentLink();
      // RODO art. 17 + art. 9 — notatka pracownika to free-text mogący zawierać dane
      // szczególnej kategorii (np. o zdrowiu); przy usuwaniu klienta ją czyścimy.
      appointment.ScrubNotes();
    }

    var appointmentIds = appointments.Select(a => a.Id).ToList();
    var shortLinks = await context.ShortLinks
      .Where(l => l.AppointmentId != null && appointmentIds.Contains(l.AppointmentId.Value))
      .ToListAsync(ct);
    context.ShortLinks.RemoveRange(shortLinks);
  }
}
