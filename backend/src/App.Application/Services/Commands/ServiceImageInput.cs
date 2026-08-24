namespace App.Application.Services.Commands;

/// <summary>
/// Pojedyncze zdjęcie galerii w komendach Create/Update usługi. Front uploaduje plik WCZEŚNIEJ
/// przez POST /api/media/images i podaje tu gotowe URL-e + klucz storage. Kolejność na liście
/// wyznacza pozycję w galerii (pierwsze = cover).
/// </summary>
public record ServiceImageInput(string Url, string ThumbnailUrl, string Key);
