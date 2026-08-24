namespace App.Application.Booking.BookingServices.Dtos;

/// <summary>
/// Zdjęcie galerii usługi w publicznym booking DTO. Bez klucza storage — klientka widzi tylko URL-e.
/// Kolejność na liście (rosnące OrderIndex) = kolejność galerii; pierwsze = cover.
/// </summary>
public record BookingServiceImageDto(string Url, string ThumbnailUrl, int OrderIndex);
