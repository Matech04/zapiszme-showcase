using MediatR;

namespace App.Application.Notifications.Push.Queries.GetVapidPublicKey;

/// <summary>Zwraca publiczny klucz VAPID do subskrypcji Web Push w przeglądarce. Pusty gdy push nieskonfigurowany.</summary>
public record GetVapidPublicKeyQuery : IRequest<VapidPublicKeyDto>;

public record VapidPublicKeyDto(string PublicKey, bool Enabled);

internal class GetVapidPublicKeyQueryHandler : IRequestHandler<GetVapidPublicKeyQuery, VapidPublicKeyDto>
{
  private readonly IWebPushKeys _keys;

  public GetVapidPublicKeyQueryHandler(IWebPushKeys keys)
  {
    _keys = keys;
  }

  public Task<VapidPublicKeyDto> Handle(GetVapidPublicKeyQuery request, CancellationToken ct)
    => Task.FromResult(new VapidPublicKeyDto(
      _keys.IsConfigured ? _keys.PublicKey : string.Empty,
      _keys.IsConfigured));
}
