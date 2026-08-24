using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Tenants.Dtos;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.SalonSettings.Commands.UpdateCurrentSalonSettings;

public record UpdateCurrentSalonSettingsRequest(
  string Name,
  string Slug,
  CustomerVerificationChannel CustomerVerificationChannel,
  int AppointmentSlotStepMinutes,
  string TimeZoneId,
  string Currency,
  BookingAccessPolicy? BookingAccessPolicy = null,
  AppointmentConfirmationMode? AppointmentConfirmationMode = null,
  GapFillingSettingsDto? GapFillingSettings = null,
  NotificationSettingsDto? NotificationSettings = null,
  StaffCalendarVisibilityPolicy? StaffCalendarVisibilityPolicy = null,
  bool? RequireCustomerName = null,
  bool? CollectInstagramHandle = null,
  bool? CollectInspirationImages = null,
  DepositSettingsDto? DepositSettings = null,
  string? BookingCalendarColorHex = null,
  string? BookingCalendarBackgroundHex = null,
  string? BookingCalendarSurfaceHex = null,
  string? BookingCalendarPriceHex = null,
  string? TermsOfService = null,
  bool? DoNotRetainAppointmentHistory = null,
  int? BookingHorizonDays = null);

public record UpdateCurrentSalonSettingsCommand(
  string Name,
  string Slug,
  CustomerVerificationChannel CustomerVerificationChannel,
  int AppointmentSlotStepMinutes,
  string TimeZoneId,
  string Currency,
  BookingAccessPolicy? BookingAccessPolicy = null,
  AppointmentConfirmationMode? AppointmentConfirmationMode = null,
  GapFillingSettingsDto? GapFillingSettings = null,
  NotificationSettingsDto? NotificationSettings = null,
  StaffCalendarVisibilityPolicy? StaffCalendarVisibilityPolicy = null,
  bool? RequireCustomerName = null,
  bool? CollectInstagramHandle = null,
  bool? CollectInspirationImages = null,
  DepositSettingsDto? DepositSettings = null,
  string? BookingCalendarColorHex = null,
  string? BookingCalendarBackgroundHex = null,
  string? BookingCalendarSurfaceHex = null,
  string? BookingCalendarPriceHex = null,
  string? TermsOfService = null,
  bool? DoNotRetainAppointmentHistory = null,
  // Ile dni naprzod klient moze rezerwowac online. Null = nie ruszaj biezacej wartosci.
  int? BookingHorizonDays = null) : IRequest;

internal class UpdateCurrentSalonSettingsCommandHandler
    : TenantHandler<UpdateCurrentSalonSettingsCommand>
{
  private readonly ITenantRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly ITenantSlugCache _slugCache;

  public UpdateCurrentSalonSettingsCommandHandler(
    ICurrentTenantService currentTenantService,
    ITenantRepository repository,
    IUnitOfWork uow,
    ITenantSlugCache slugCache)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
    _slugCache = slugCache;
  }

  public override async Task Handle(UpdateCurrentSalonSettingsCommand request, CancellationToken ct)
  {
    var tenant = await _repository.GetByIdAsync(TenantId);

    if (tenant == null)
    {
      throw new NotFoundException(nameof(Tenant), TenantId);
    }

    // Slug PRZED zmiana — potrzebny do unieważnienia cache'u slug→tenant po zapisie.
    var previousSlug = tenant.Slug;

    GapFillingSettings? gapFilling = null;
    if (request.GapFillingSettings != null)
    {
      gapFilling = new GapFillingSettings(
        request.GapFillingSettings.Mode,
        request.GapFillingSettings.BufferMinutes,
        request.GapFillingSettings.LookaheadSlots);
    }

    NotificationSettings? notificationSettings = null;
    if (request.NotificationSettings is { } ns)
    {
      notificationSettings = new NotificationSettings(
        ns.NewBookingToSalon,
        ns.BookingConfirmationToCustomer,
        ns.CancellationToSalon,
        ns.CancellationToCustomer,
        ns.RescheduleToSalon,
        ns.RescheduleToCustomer,
        ns.AppointmentReminderToCustomer,
        ns.AwaitingConfirmationToSalon,
        ns.CancelledBySalonToCustomer,
        ns.RescheduledBySalonToCustomer,
        ns.AppointmentReminder2hToCustomer,
        ns.StaffBookedAppointmentToCustomer);
    }

    DepositSettings? depositSettings = null;
    if (request.DepositSettings is { } ds)
    {
      depositSettings = new DepositSettings(ds.Enabled, ds.Mode, ds.Value, ds.Instrument);
    }

    tenant.Update(
      request.Name,
      request.Slug,
      request.CustomerVerificationChannel,
      request.AppointmentSlotStepMinutes,
      request.TimeZoneId,
      request.Currency,
      request.BookingAccessPolicy,
      request.AppointmentConfirmationMode,
      gapFilling,
      notificationSettings,
      request.StaffCalendarVisibilityPolicy,
      request.RequireCustomerName,
      request.CollectInstagramHandle,
      request.CollectInspirationImages,
      depositSettings,
      request.BookingCalendarColorHex,
      request.BookingCalendarBackgroundHex,
      request.BookingCalendarSurfaceHex,
      request.BookingCalendarPriceHex,
      request.TermsOfService,
      request.DoNotRetainAppointmentHistory,
      request.BookingHorizonDays);
    _repository.Update(tenant);
    await _uow.SaveChangesAsync(ct);

    // Unieważnienie PO udanym zapisie (przed nim cofnięta transakcja zostawiłaby pusty cache
    // z poprawnym slugiem — nieszkodliwe, ale mylące). Czyścimy STARY i NOWY: stary, bo zwolniony
    // slug może przejąć inny salon; nowy, bo mógł tam wisieć wpis poprzedniego właściciela tej nazwy.
    if (!string.Equals(previousSlug, tenant.Slug, StringComparison.Ordinal))
    {
      _slugCache.Invalidate(previousSlug);
      _slugCache.Invalidate(tenant.Slug);
    }
  }
}
