using App.Application.Common.Security;

namespace App.Api.E2eSupport;

/// <summary>
/// In-memory zastępstwo <see cref="IPhoneOtpSender"/> w testach integracyjnych. Łapie kody
/// żeby test mógł je odczytać zamiast pukać do prawdziwego smsapi.pl.
/// </summary>
public sealed class TestPhoneOtpMailbox : IPhoneOtpSender
{
  private readonly List<(string Phone, string Code, DateTime At)> _calls = new();
  private readonly object _lock = new();

  public IReadOnlyList<(string Phone, string Code, DateTime At)> Calls
  {
    get
    {
      lock (_lock) { return _calls.ToArray(); }
    }
  }

  public (string Phone, string Code) Last
  {
    get
    {
      lock (_lock) { return (_calls[^1].Phone, _calls[^1].Code); }
    }
  }

  public string? LastCodeForPhone(string phone)
  {
    lock (_lock)
    {
      for (var i = _calls.Count - 1; i >= 0; i--)
      {
        if (_calls[i].Phone == phone) { return _calls[i].Code; }
      }
      return null;
    }
  }

  public void Clear()
  {
    lock (_lock) { _calls.Clear(); }
  }

  public Task SendOtpAsync(string phoneE164, string code, CancellationToken ct)
  {
    lock (_lock) { _calls.Add((phoneE164, code, DateTime.UtcNow)); }
    return Task.CompletedTask;
  }
}
