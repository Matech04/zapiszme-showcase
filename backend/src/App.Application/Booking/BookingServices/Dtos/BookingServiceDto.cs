using App.Domain.Common;

namespace App.Application.Booking.BookingServices.Dtos;

/// <summary>
/// Public booking DTO usługi. <see cref="CategoryId"/> jest opcjonalne — usługi
/// bez kategorii (orphans) wpadają w sekcję "Inne usługi" w BookingPanel.svelte.
/// </summary>
public record BookingServiceDto(
    Guid Id,
    Guid? CategoryId,
    string Name,
    Money Price,
    int DurationInMinutes,
    decimal? MaxAmount = null,
    int? DurationMinMinutes = null,
    int? DurationMaxMinutes = null,
    string? ComboGroup = null,
    bool HidePrice = false,
    int OrderIndex = 0,
    bool IsAddon = false,
    List<Guid>? AddonServiceIds = null,
    string? Description = null,
    List<BookingServiceImageDto>? Images = null);
