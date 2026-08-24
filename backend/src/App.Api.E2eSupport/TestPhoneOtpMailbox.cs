using App.Application.Common.Security;

namespace App.Api.E2eSupport;

/// <summary>
/// In-memory zastępstwo <see cref="IPhoneOtpSender"/> w środowisku <c>E2E</c>. Przechwytuje
/// pary (telefon, kod), żeby backdoor <c>/api/_e2e/phone-otp/{userId}</c> mógł je zwrócić
/// do Playwrighta zamiast pukać do prawdziwego smsapi.pl.
///
/// UserId nie jest tu przekazywany — handler woła <c>SendOtpAsync(phoneE164, code)</c>.
/// E2eController łączy phone → userId po stronie czytania z DB.
/// </summary>
public sealed class TestPhoneOtpMailbox : IPhoneOtpSender
{
  private readonly List<(string Phone, string Code, DateTime At)> _calls = new();
  private readonly object _gate = new();

  public IReadOnlyList<(string Phone, string Code, DateTime At)> Calls
  {
    get
    {
      lock (_gate) { return _calls.ToArray(); }
    }
  }

  public string? LastCodeForPhone(string phoneE164)
  {
    lock (_gate)
    {
      for (var i = _calls.Count - 1; i >= 0; i--)
      {
        if (_calls[i].Phone == phoneE164) { return _calls[i].Code; }
      }
      return null;
    }
  }

  public void Clear()
  {
    lock (_gate) { _calls.Clear(); }
  }

  public Task SendOtpAsync(string phoneE164, string code, CancellationToken ct)
  {
    lock (_gate) { _calls.Add((phoneE164, code, DateTime.UtcNow)); }
    return Task.CompletedTask;
  }
}
