using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Notifications.SmsTemplates.Commands.SubmitSmsTemplate;

/// <summary>
/// Salon zapisuje treść custom szablonu SMS dla danego typu → trafia do kolejki „oczekuje na akceptację".
/// Do czasu akceptacji wysyłany jest poprzedni zatwierdzony szablon albo domyślny hardcoded.
/// Tenant-scoped (BusinessManagement).
/// </summary>
public record SubmitSmsTemplateCommand(NotificationType NotificationType, string Body) : IRequest;

public class SubmitSmsTemplateCommandHandler : TenantHandler<SubmitSmsTemplateCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;
  private readonly ISmsTemplateModerationNotifier _moderationNotifier;
  private readonly ILogger<SubmitSmsTemplateCommandHandler> _logger;

  public SubmitSmsTemplateCommandHandler(
    ICurrentTenantService currentTenantService,
    IApplicationDbContext context,
    TimeProvider timeProvider,
    ISmsTemplateModerationNotifier moderationNotifier,
    ILogger<SubmitSmsTemplateCommandHandler> logger) : base(currentTenantService)
  {
    _context = context;
    _timeProvider = timeProvider;
    _moderationNotifier = moderationNotifier;
    _logger = logger;
  }

  public override async Task Handle(SubmitSmsTemplateCommand request, CancellationToken ct)
  {
    // Reguły biznesowe treści (typ personalizowalny, długość wg kodowania, dozwolone placeholdery).
    SmsTemplateValidator.Validate(request.NotificationType, request.Body);

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    var template = await _context.SmsTemplates
      .FirstOrDefaultAsync(t => t.NotificationType == request.NotificationType, ct);

    if (template is null)
    {
      template = SmsTemplate.Create(TenantId, request.NotificationType);
      _context.SmsTemplates.Add(template);
    }

    template.SubmitForApproval(request.Body, nowUtc);

    await _context.SaveChangesAsync(ct);

    // Powiadom moderatora platformy (e-mail, np. kontakt@zapisz.me) o nowym szablonie do akceptacji.
    // BEST-EFFORT — szablon jest już zapisany, więc błąd maila NIE może wywrócić tej operacji.
    try
    {
      var salonName = await _context.Tenants
        .Where(t => t.Id == TenantId)
        .Select(t => t.Name)
        .FirstOrDefaultAsync(ct);

      await _moderationNotifier.NotifySubmittedForApprovalAsync(
        salonName ?? "Salon", request.NotificationType, request.Body, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Powiadomienie e-mail o szablonie SMS do moderacji nie powiodło się.");
    }
  }
}
