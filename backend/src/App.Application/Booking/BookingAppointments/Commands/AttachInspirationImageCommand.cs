using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Booking.BookingAppointments.Commands;

/// <summary>
/// Dołącza POJEDYNCZE zdjęcie inspiracji do JUŻ POTWIERDZONEJ wizyty (deferred-upload). Front trzyma
/// zdjęcia w przeglądarce do chwili potwierdzenia OTP, a dopiero potem uploaduje je tu, autoryzując
/// żądanie krótkożyjącym, podpisanym tokenem grantu (<see cref="IInspirationUploadTokenService"/>)
/// wydanym przez verify-otp / confirm-with-session. Dzięki temu na storage trafiają WYŁĄCZNIE zdjęcia
/// dla potwierdzonych rezerwacji — zero sierot, brak otwartego endpointu do spamowania uploadem.
///
/// Kolejność jest istotna dla braku sierot: walidacja tokenu i twardy cap (<see
/// cref="Appointment.MaxInspirationImages"/>) sprawdzane są PRZED obróbką/wgraniem obrazu — odrzucone
/// żądanie nie zostawia osieroconego obiektu w buckecie.
/// </summary>
public record AttachInspirationImageCommand(
    Guid AppointmentId,
    string GrantToken,
    Stream ImageStream) : IRequest<AttachInspirationImageResult>;

/// <summary>Wynik dołączenia: klucz głównego obiektu + publiczne URL-e głównego obrazu i miniatury.</summary>
public record AttachInspirationImageResult(string Url, string ThumbnailUrl, string Key);

internal sealed class AttachInspirationImageCommandHandler
    : IRequestHandler<AttachInspirationImageCommand, AttachInspirationImageResult>
{
  private readonly IAppointmentRepository _repository;
  private readonly ITenantRepository _tenantRepository;
  private readonly IInspirationUploadTokenService _uploadTokens;
  private readonly IImageProcessingService _imageProcessing;
  private readonly IUnitOfWork _uow;
  private readonly ICurrentTenantService _currentTenant;

  public AttachInspirationImageCommandHandler(
      IAppointmentRepository repository,
      ITenantRepository tenantRepository,
      IInspirationUploadTokenService uploadTokens,
      IImageProcessingService imageProcessing,
      IUnitOfWork uow,
      ICurrentTenantService currentTenant)
  {
    _repository = repository;
    _tenantRepository = tenantRepository;
    _uploadTokens = uploadTokens;
    _imageProcessing = imageProcessing;
    _uow = uow;
    _currentTenant = currentTenant;
  }

  public async Task<AttachInspirationImageResult> Handle(AttachInspirationImageCommand request, CancellationToken ct)
  {
    // Grant musi być ważny (podpis + TTL) i wystawiony DOKŁADNIE dla tej wizyty — inaczej cudzy/wygasły
    // token nie da się użyć do podpięcia zdjęć pod obcą rezerwację.
    if (!_uploadTokens.TryValidate(request.GrantToken, out var tokenAppointmentId)
        || tokenAppointmentId != request.AppointmentId)
    {
      throw new ForbiddenAccessException(
          "Nieprawidłowy lub wygasły token uploadu zdjęć inspiracji.",
          ErrorCodes.AppointmentInspirationUploadForbidden);
    }

    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Defense-in-depth: wizyta MUSI należeć do tenanta ze slugu (query-filter już to gwarantuje).
    if (_currentTenant.TenantId is { } currentTenantId && appointment.TenantId != currentTenantId)
    {
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    // Bramka funkcji: salon mógł wyłączyć zbieranie inspiracji — wtedy serwer odrzuca upload nawet
    // gdy ktoś spreparuje żądanie (front i tak ukrywa picker). Brak sieroty: sprawdzane przed uploadem.
    var tenant = await _tenantRepository.GetByIdAsync(appointment.TenantId)
        ?? throw new NotFoundException(nameof(Tenant), appointment.TenantId);
    if (!tenant.CollectInspirationImages)
    {
      throw new ForbiddenAccessException(
          "Salon nie zbiera zdjęć inspiracji.",
          ErrorCodes.AppointmentInspirationUploadForbidden);
    }

    // Twardy cap PRZED uploadem — odrzucone żądanie nie zostawia sieroty w buckecie.
    if (appointment.InspirationImages.Count >= Appointment.MaxInspirationImages)
    {
      throw new AppointmentBookingRuleException(
          $"Można dodać maksymalnie {Appointment.MaxInspirationImages} zdjęć inspiracji.",
          ErrorCodes.AppointmentTooManyInspirationImages);
    }

    // Obróbka + zapis (magic-bytes, EXIF strip, WebP + miniatura) pod prefiksem inspirations/.
    var processed = await _imageProcessing.ProcessAndStoreAsync(request.ImageStream, "inspirations", ct);

    appointment.AddInspirationImage(
        new AppointmentInspirationLine(processed.Url, processed.ThumbnailUrl, processed.Key, processed.ThumbnailKey));

    await _uow.SaveChangesAsync(ct);

    return new AttachInspirationImageResult(processed.Url, processed.ThumbnailUrl, processed.Key);
  }
}
