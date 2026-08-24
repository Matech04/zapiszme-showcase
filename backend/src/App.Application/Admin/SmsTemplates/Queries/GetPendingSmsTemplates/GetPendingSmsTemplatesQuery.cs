using App.Application.Admin.SmsTemplates.Dtos;
using App.Application.Common.Interfaces;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.SmsTemplateAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.SmsTemplates.Queries.GetPendingSmsTemplates;

/// <summary>
/// Adminowa kolejka moderacji: wszystkie custom szablony SMS oczekujące na akceptację, CROSS-TENANT.
/// NIE jest tenant-scoped — admin (SystemAdmin) nie ma kontekstu tenanta, więc świadomie używamy
/// <c>IgnoreQueryFilters</c> (admin czyta ponad izolacją tenantów; to dane do moderacji platformy).
/// </summary>
public record GetPendingSmsTemplatesQuery() : IRequest<List<PendingSmsTemplateDto>>;

public class GetPendingSmsTemplatesQueryHandler : IRequestHandler<GetPendingSmsTemplatesQuery, List<PendingSmsTemplateDto>>
{
  private readonly IApplicationDbContext _context;
  private readonly ISmsDefaultTemplateProvider _preview;

  public GetPendingSmsTemplatesQueryHandler(IApplicationDbContext context, ISmsDefaultTemplateProvider preview)
  {
    _context = context;
    _preview = preview;
  }

  public async Task<List<PendingSmsTemplateDto>> Handle(GetPendingSmsTemplatesQuery request, CancellationToken ct)
  {
    // Cross-tenant: admin nie jest scope'owany — jawny IgnoreQueryFilters (świadomy wyjątek od reguły).
    var rows = await _context.SmsTemplates
      .AsNoTracking()
      .IgnoreQueryFilters()
      .Where(t => t.Status == SmsTemplateStatus.PendingApproval && t.PendingBody != null)
      .Join(
        _context.Tenants.AsNoTracking().IgnoreQueryFilters(),
        t => t.TenantId,
        tenant => tenant.Id,
        (t, tenant) => new
        {
          t.Id,
          t.TenantId,
          TenantName = tenant.Name,
          t.NotificationType,
          t.PendingBody,
          t.SubmittedAtUtc,
        })
      .OrderBy(x => x.SubmittedAtUtc)
      .ToListAsync(ct);

    return rows
      .Select(x => new PendingSmsTemplateDto(
        TemplateId: x.Id,
        TenantId: x.TenantId,
        TenantName: x.TenantName,
        NotificationType: x.NotificationType,
        TypeLabel: SmsTemplateLabels.For(x.NotificationType),
        PendingBody: x.PendingBody!,
        PendingPreview: _preview.RenderPreview(x.NotificationType, x.PendingBody!),
        SubmittedAtUtc: x.SubmittedAtUtc))
      .ToList();
  }
}
