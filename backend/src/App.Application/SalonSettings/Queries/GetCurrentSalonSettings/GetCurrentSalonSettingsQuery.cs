using App.Application.Tenants.Dtos;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.SalonSettings.Queries.GetCurrentSalonSettings;

public record GetCurrentSalonSettingsQuery : IRequest<TenantDto>;

internal class GetCurrentSalonSettingsQueryHandler
    : TenantHandler<GetCurrentSalonSettingsQuery, TenantDto>
{
  private readonly IApplicationDbContext _context;
  private readonly IPlatformMaintenanceState _maintenanceState;

  public GetCurrentSalonSettingsQueryHandler(
    ICurrentTenantService currentTenantService,
    IApplicationDbContext context,
    IPlatformMaintenanceState maintenanceState)
    : base(currentTenantService)
  {
    _context = context;
    _maintenanceState = maintenanceState;
  }

  public override async Task<TenantDto> Handle(GetCurrentSalonSettingsQuery request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .AsNoTracking()
        .Where(t => t.Id == TenantId)
        .FirstOrDefaultAsync(ct);

    if (tenant == null)
    {
      throw new NotFoundException("Tenant", TenantId);
    }

    GapFillingSettingsDto? gapFillingDto = null;
    if (tenant.GapFillingSettings != null)
    {
      gapFillingDto = new GapFillingSettingsDto(
        tenant.GapFillingSettings.Mode,
        tenant.GapFillingSettings.BufferMinutes,
        tenant.GapFillingSettings.LookaheadSlots);
    }

    var notificationDto = new NotificationSettingsDto(
      tenant.NotificationSettings.NewBookingToSalon,
      tenant.NotificationSettings.BookingConfirmationToCustomer,
      tenant.NotificationSettings.CancellationToSalon,
      tenant.NotificationSettings.CancellationToCustomer,
      tenant.NotificationSettings.RescheduleToSalon,
      tenant.NotificationSettings.RescheduleToCustomer,
      tenant.NotificationSettings.AppointmentReminderToCustomer,
      tenant.NotificationSettings.AwaitingConfirmationToSalon,
      tenant.NotificationSettings.CancelledBySalonToCustomer,
      tenant.NotificationSettings.RescheduledBySalonToCustomer,
      tenant.NotificationSettings.AppointmentReminder2hToCustomer,
      tenant.NotificationSettings.StaffBookedAppointmentToCustomer);

    var depositDto = new DepositSettingsDto(
      tenant.DepositSettings.Enabled,
      tenant.DepositSettings.Mode,
      tenant.DepositSettings.Value,
      tenant.DepositSettings.Instrument);

    var maintenance = await _maintenanceState.GetAsync(ct);

    var merchantDto = tenant.MerchantAccount is null
      ? MerchantAccountStatusDto.NotConnected()
      : new MerchantAccountStatusDto(
          true,
          tenant.MerchantAccount.Provider,
          tenant.MerchantAccount.OnboardingStatus,
          tenant.MerchantAccount.CanAcceptPayments);

    return new TenantDto(
      tenant.Id,
      tenant.Name,
      tenant.Slug,
      tenant.CustomerVerificationChannel,
      tenant.AppointmentSlotStepMinutes,
      tenant.TimeZoneId,
      tenant.Currency,
      tenant.BookingAccessPolicy,
      tenant.AppointmentConfirmationMode,
      gapFillingDto,
      notificationDto,
      tenant.StaffCalendarVisibilityPolicy,
      tenant.RequireCustomerName,
      tenant.CollectInstagramHandle,
      tenant.CollectInspirationImages,
      depositDto,
      merchantDto,
      BookingCalendarColorHex: tenant.BookingCalendarColorHex,
      BookingCalendarBackgroundHex: tenant.BookingCalendarBackgroundHex,
      BookingCalendarSurfaceHex: tenant.BookingCalendarSurfaceHex,
      BookingCalendarPriceHex: tenant.BookingCalendarPriceHex,
      TermsOfService: tenant.TermsOfService,
      DoNotRetainAppointmentHistory: tenant.DoNotRetainAppointmentHistory,
      BookingPaused: tenant.BookingPaused,
      BookingPauseMessage: tenant.BookingPauseMessage,
      PlatformMaintenance: maintenance.Enabled,
      BookingHorizonDays: tenant.BookingHorizonDays);
  }
}
