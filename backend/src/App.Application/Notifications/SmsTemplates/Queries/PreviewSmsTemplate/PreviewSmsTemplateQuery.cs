using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;

namespace App.Application.Notifications.SmsTemplates.Queries.PreviewSmsTemplate;

/// <summary>
/// Podgląd FINALNEJ treści SMS dla danego szablonu. Przepuszcza <paramref name="Body"/> przez TĘ SAMĄ
/// ścieżkę co wysyłka: interpolacja placeholderów przykładowymi danymi (<c>SamplePayload</c>) +
/// transliteracja ASCII/GSM-7 + clip do 140 (<c>SmsTextBuilder.Sanitize</c>). Dzięki temu owner widzi
/// DOKŁADNIE to, co dostanie klient — np. „ż" → „z", przycięcie długiej nazwy usługi, podstawione daty.
/// (Pusty body = klient i tak dostanie domyślny szablon; front pokazuje wtedy <c>DefaultPreview</c>.)
/// </summary>
public record PreviewSmsTemplateQuery(NotificationType NotificationType, string Body)
  : IRequest<SmsTemplatePreviewResult>;

public record SmsTemplatePreviewResult(string Preview);

internal sealed class PreviewSmsTemplateQueryHandler
  : IRequestHandler<PreviewSmsTemplateQuery, SmsTemplatePreviewResult>
{
  private readonly ISmsDefaultTemplateProvider _preview;

  public PreviewSmsTemplateQueryHandler(ISmsDefaultTemplateProvider preview)
  {
    _preview = preview;
  }

  public Task<SmsTemplatePreviewResult> Handle(PreviewSmsTemplateQuery request, CancellationToken ct)
  {
    var text = _preview.RenderPreview(request.NotificationType, request.Body ?? string.Empty);
    return Task.FromResult(new SmsTemplatePreviewResult(text));
  }
}
