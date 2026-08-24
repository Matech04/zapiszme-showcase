using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.Tenants.Dtos;

public record TenantDto(
  Guid Id,
  string Name,
  string Slug,
  CustomerVerificationChannel CustomerVerificationChannel,
  int AppointmentSlotStepMinutes,
  string TimeZoneId,
  string Currency,
  BookingAccessPolicy BookingAccessPolicy,
  AppointmentConfirmationMode AppointmentConfirmationMode,
  GapFillingSettingsDto? GapFillingSettings = null,
  NotificationSettingsDto? NotificationSettings = null,
  StaffCalendarVisibilityPolicy StaffCalendarVisibilityPolicy = StaffCalendarVisibilityPolicy.OwnCalendarOnly,
  bool RequireCustomerName = false,
  bool CollectInstagramHandle = false,
  bool CollectInspirationImages = false,
  DepositSettingsDto? DepositSettings = null,
  MerchantAccountStatusDto? MerchantAccount = null,
  string? CustomDomain = null,
  string? BookingCalendarColorHex = null,
  string? BookingCalendarBackgroundHex = null,
  string? BookingCalendarSurfaceHex = null,
  string? BookingCalendarPriceHex = null,
  string? TermsOfService = null,
  bool DoNotRetainAppointmentHistory = false,
  bool BookingPaused = false,
  string? BookingPauseMessage = null,
  bool PlatformMaintenance = false,
  // Ile dni naprzod klient moze rezerwowac online (patrz Tenant.BookingHorizonDays).
  int BookingHorizonDays = 120);
