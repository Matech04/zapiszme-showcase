using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Sms;
using App.Application.Notifications.SmsTemplates.Dtos;
using App.Domain.Aggregates.SmsTemplateAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.SmsTemplates.Queries.GetSmsTemplates;

/// <summary>
/// Lista personalizowalnych typów SMS bieżącego salonu — każdy typ z domyślnym podglądem, statusem
/// workflow, treścią zatwierdzoną/oczekującą i dozwolonymi placeholderami. Tenant-scoped.
/// </summary>
public record GetSmsTemplatesQuery() : IRequest<List<SmsTemplateDto>>;

public class GetSmsTemplatesQueryHandler : TenantHandler<GetSmsTemplatesQuery, List<SmsTemplateDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly ISmsDefaultTemplateProvider _defaults;

  public GetSmsTemplatesQueryHandler(
    ICurrentTenantService currentTenantService,
    IApplicationDbContext context,
    ISmsDefaultTemplateProvider defaults) : base(currentTenantService)
  {
    _context = context;
    _defaults = defaults;
  }

  public override async Task<List<SmsTemplateDto>> Handle(GetSmsTemplatesQuery request, CancellationToken ct)
  {
    // Query filter zawęża do bieżącego tenanta; TenantId odwołany jawnie, by upewnić się o kontekście.
    _ = TenantId;

    var stored = await _context.SmsTemplates
      .AsNoTracking()
      .ToDictionaryAsync(t => t.NotificationType, t => t, ct);

    var result = new List<SmsTemplateDto>(SmsTemplatePlaceholders.CustomizableTypes.Count);
    foreach (var type in SmsTemplatePlaceholders.CustomizableTypes)
    {
      stored.TryGetValue(type, out var t);
      result.Add(new SmsTemplateDto(
        NotificationType: type,
        TypeLabel: SmsTemplateLabels.For(type),
        DefaultPreview: _defaults.GetDefaultPreview(type),
        ApprovedBody: t?.Body,
        PendingBody: t?.PendingBody,
        Status: t?.Status ?? SmsTemplateStatus.None,
        RejectionReason: t?.RejectionReason,
        SubmittedAtUtc: t?.SubmittedAtUtc,
        ReviewedAtUtc: t?.ReviewedAtUtc,
        AllowedPlaceholders: SmsTemplatePlaceholders.AllowedFor(type)));
    }

    return result;
  }
}
