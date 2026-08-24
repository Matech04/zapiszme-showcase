namespace App.Infrastructure.Notifications.Sms;

/// <summary>Wynik wysyłki pojedynczego SMS — kluczowe pola z odpowiedzi smsapi.pl.</summary>
public sealed record SmsSendResult(string Id, string Status, decimal Points);
