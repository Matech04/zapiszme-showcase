namespace App.Application.Services.Dtos;

/// <summary>
/// Zdjęcie galerii usługi w DTO. <see cref="StorageKey"/> potrzebny dashboardowi, by przy edycji
/// odesłać niezmienione zdjęcia z powrotem w komendzie (reconcile bez ponownego uploadu).
/// Kolejność na liście (rosnące OrderIndex) = kolejność galerii; pierwsze = cover.
/// </summary>
public record ServiceImageDto(string Url, string ThumbnailUrl, string StorageKey, int OrderIndex);
