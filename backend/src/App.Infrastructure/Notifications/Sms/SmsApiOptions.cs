namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Konfiguracja klienta smsapi.pl. Sekcja <c>Sms</c> w <c>appsettings.json</c>; token ładowany
/// z sekretu środowiskowego (<c>SMSAPI__OAUTH_TOKEN</c> → <c>Sms:OAuthToken</c>).
/// </summary>
public sealed class SmsApiOptions
{
  /// <summary>Baza URL endpointu HTTP. Default <c>https://api.smsapi.pl/</c>.</summary>
  public string BaseUrl { get; set; } = "https://api.smsapi.pl/";

  /// <summary>OAuth bearer token wygenerowany w panelu smsapi.pl. Puste = kanał wyłączony.</summary>
  public string OAuthToken { get; set; } = string.Empty;

  /// <summary>Pole nadawcy (max 11 znaków, pre-zarejestrowane w smsapi.pl). Default <c>INFO</c>.</summary>
  public string SenderName { get; set; } = "INFO";

  /// <summary>
  /// Gdy <c>true</c>, do każdego żądania doklejamy <c>test=1</c> — smsapi.pl symuluje wysyłkę
  /// bez naliczania kredytów. Zalecane w Development.
  /// </summary>
  public bool TestMode { get; set; }

  /// <summary>Timeout HTTP w sekundach. Default 10s.</summary>
  public int TimeoutSeconds { get; set; } = 10;

  /// <summary>
  /// Gdy <c>true</c>, link akcji w treści SMS-a idzie jako <c>[%idzdo:url%]</c> — smsapi podmienia go
  /// na skrót na własnej domenie <c>idz.do</c>. Powód: konta bez przyznanej zgody na linki dostają
  /// z API błąd 94 („Not allowed to send messages with link") i SMS z zadatkiem nigdy nie wychodzi.
  /// Default <c>true</c>: surowy URL na naszym koncie jest twardo odrzucany, więc skracacz to jedyna
  /// ścieżka, którą SMS z zadatkiem w ogóle wychodzi. Ustaw <c>false</c> (env
  /// <c>Sms__UseIdzDoShortener=false</c>), gdy smsapi przyzna koncie zgodę na surowe linki albo gdy
  /// skracacz zacznie sprawiać problemy — to wyłącznik awaryjny bez deployu. Dotyczy wyłącznie
  /// powiadomień z <c>ActionUrl</c> (dziś: link do zadatku).
  /// </summary>
  public bool UseIdzDoShortener { get; set; } = true;

  /// <summary>
  /// Globalny (cross-tenant) twardy limit liczby SMS wysłanych w ciągu doby UTC — kill-switch
  /// chroniący przed drenażem kredytów smsapi.pl, którego per-tenant capy nie łapią (np. masowa
  /// rejestracja kont = wiele SMS bez tenanta). Po przekroczeniu klient rzuca i NIE wysyła.
  /// 0 = wyłączony. Liczony w pamięci procesu (single-instance MVP) — przy skalowaniu poziomym
  /// przenieść do współdzielonego store. Default 500/dobę (z zapasem na pierwszych testerów).
  /// </summary>
  public int GlobalDailyCap { get; set; } = 500;

  /// <summary>
  /// Maks. liczba SMS wysłanych z JEDNEGO adresu IP w ciągu doby UTC — gruby backstop ograniczający
  /// pojedynczy (nierotujący) IP atakującego do ułamka globalnego budżetu. Liczony tylko dla SMS
  /// inicjowanych żądaniem HTTP (booking/self-service/rejestracja); tło (przypomnienia) pomijane
  /// (brak HttpContext). 0 = wyłączony. UWAGA CGNAT: operatorzy komórkowi/biura dzielą jeden IP —
  /// jeśli pojawią się false-positivy legalnych użytkowników, podnieś tę wartość. Default 50/dobę.
  /// </summary>
  public int MaxSmsPerIpPerDay { get; set; } = 50;
}
