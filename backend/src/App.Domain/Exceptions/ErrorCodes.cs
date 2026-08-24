namespace App.Domain.Exceptions;

public interface IErrorCodeException
{
  string ErrorCode { get; }
}

public static class ErrorCodes
{

  public const string InvalidDateRange = "date_range.invalid";
  public const string InvalidTimeRange = "time_range.invalid";

  public const string ValidationFailed = "validation.failed";
  public const string InvalidArgument = "validation.invalid_argument";
  public const string NotFound = "resource.not_found";
  public const string Unauthorized = "auth.unauthorized";
  public const string Forbidden = "auth.forbidden";
  /// <summary>Token antiforgery nieobecny lub nieważny — klient musi odświeżyć stronę, nie zalogować się ponownie.</summary>
  public const string AntiforgeryInvalid = "auth.antiforgery_invalid";
  public const string TenantViolation = "tenant.violation";
  public const string TenantMissing = "tenant.missing";
  public const string CustomDomainAlreadyAssigned = "tenant.custom_domain.already_assigned";
  public const string SalonSlugTaken = "tenant.slug_taken";
  public const string IdentityEmployeeMissing = "auth.identity_employee_missing";
  public const string RateLimitExceeded = "rate_limit.exceeded";
  public const string PersistenceFailed = "persistence.failed";
  public const string InternalError = "internal.error";

  public const string AppointmentInvalidTimeRange = "appointment.invalid_time_range";
  public const string AppointmentSlotUnavailable = "appointment.slot_unavailable";
  public const string AppointmentInvalidStatus = "appointment.invalid_status";
  public const string AppointmentCompletedCannotBeCanceled = "appointment.completed_cannot_be_canceled";
  public const string AppointmentFinalPriceInvalidStatus = "appointment.final_price.invalid_status";
  public const string AppointmentServicesChangeInvalidStatus = "appointment.services_change.invalid_status";
  public const string AppointmentNoServices = "appointment.no_services";
  public const string AppointmentTooManyServices = "appointment.too_many_services";
  public const string AppointmentDuplicateService = "appointment.duplicate_service";
  public const string AppointmentTooManyInspirationImages = "appointment.too_many_inspiration_images";
  public const string AppointmentInspirationUploadForbidden = "appointment.inspiration.upload_forbidden";
  public const string AppointmentComboGroupConflict = "appointment.combo_group_conflict";
  public const string AppointmentZeroDuration = "appointment.zero_duration";
  public const string AppointmentInvalidDuration = "appointment.invalid_duration";
  public const string AppointmentAddonRequiresMain = "appointment.addon_requires_main";
  public const string AppointmentAddonNotAllowed = "appointment.addon_not_allowed";
  public const string ServiceAddonInvalid = "service.addon_invalid";
  public const string AppointmentSwapMultiServiceUnsupported = "appointment.swap.multi_service_unsupported";
  public const string AppointmentOtpInvalidLease = "appointment.otp.invalid_lease";
  public const string AppointmentOtpMissingContact = "appointment.otp.missing_contact";
  public const string AppointmentOtpUnsupportedPhoneRegion = "appointment.otp.unsupported_phone_region";
  public const string AppointmentOtpMissingName = "appointment.otp.missing_name";
  public const string AppointmentOtpVerificationRequired = "appointment.otp.verification_required";
  public const string AppointmentOtpTooManyFailures = "appointment.otp.too_many_failures";
  public const string AppointmentOtpInvalidCode = "appointment.otp.invalid_code";
  public const string AppointmentSessionContactMismatch = "appointment.session.contact_mismatch";
  public const string AppointmentSwapTerminalStatus = "appointment.swap.terminal_status";
  public const string AppointmentSwapSameAppointment = "appointment.swap.same_appointment";
  public const string AppointmentSwapHarmonizationUnavailable = "appointment.swap.harmonization_unavailable";

  public const string EmployeeServiceAlreadyAssigned = "employee.service_already_assigned";
  public const string EmployeeServiceMissing = "employee.service_missing";
  public const string EmployeeNoLinkedAccount = "employee.no_linked_account";
  public const string EmployeeCannotMutateOtherProfile = "employee.cannot_mutate_other_profile";

  // Kalendarz zespołu — odmowa zależna od `StaffCalendarVisibilityPolicy` salonu, nie od roli.
  public const string CalendarCannotViewOtherEmployee = "calendar.cannot_view_other_employee";
  public const string CalendarCannotMutateOtherEmployee = "calendar.cannot_mutate_other_employee";

  public const string LeaveInvalidDates = "leave.invalid_dates";
  public const string LeaveOverlap = "leave.overlap";
  public const string LeaveOnlyUpcomingMayBeRemoved = "leave.only_upcoming_may_be_removed";

  public const string ScheduleOverlappingShifts = "schedule.overlapping_shifts";
  public const string ScheduleBreakNotWithinWorkRange = "schedule.break_not_within_work_range";
  public const string ScheduleInvalidDaysCount = "schedule.invalid_days_count";
  public const string ScheduleInvalidCycleIndex = "schedule.invalid_cycle_index";
  public const string ScheduleDaysCollision = "schedule.days_collision";
  public const string SchedulesCollision = "schedule.schedules_collision";

  public const string CustomerVerificationPhoneDisabled = "customer_verification.phone_disabled";
  public const string BookingNotInvited = "booking.not_invited";
  public const string BookingUnavailable = "booking.unavailable";
  public const string BookingPaused = "booking.paused";
  public const string CurrencyInvalidLength = "currency.invalid_length";
  public const string ValueTooLong = "validation.value_too_long";

  public const string PhoneNotConfirmed = "auth.phone_not_confirmed";
  public const string PhoneOtpInvalid = "auth.phone_otp_invalid";
  public const string PhoneOtpExpired = "auth.phone_otp_expired";
  public const string PhoneOtpLocked = "auth.phone_otp_locked";
  public const string PhoneOtpCooldown = "auth.phone_otp_cooldown";
  public const string PhoneOtpAlreadyConfirmed = "auth.phone_already_confirmed";
  public const string PhoneOtpEmailNotConfirmed = "auth.phone_email_not_confirmed";
  public const string SmsServiceUnavailable = "auth.sms_service_unavailable";
  public const string RegistrationConflict = "auth.registration_conflict";
  public const string IdentityOperationFailed = "auth.identity_operation_failed";
  public const string OnboardingNotVerified = "onboarding.not_verified";

  public const string ImpersonationTenantNotFound = "impersonation.tenant_not_found";
  public const string ImpersonationTenantDemo = "impersonation.tenant_demo";
  public const string ImpersonationReadOnly = "impersonation.read_only";
  public const string ImpersonationSessionInactive = "impersonation.session_inactive";

  public const string DepositAlreadyPaid = "deposit.already_paid";
  public const string DepositNotPaid = "deposit.not_paid";
  public const string DepositOnTerminalAppointment = "deposit.terminal_appointment";
  public const string DepositNotEnabled = "deposit.not_enabled";
  public const string MerchantAccountNotConnected = "merchant_account.not_connected";
  public const string MerchantAccountNotReady = "merchant_account.not_ready";
  public const string DepositLinkNotGenerated = "deposit.link_not_generated";
  public const string DepositCustomerContactMissing = "deposit.customer_contact_missing";
  public const string DepositSmsCapReached = "deposit.sms_cap_reached";
  public const string DepositAmountExceedsTotal = "deposit.amount_exceeds_total";
  public const string DepositSendFailed = "deposit.send_failed";
  public const string DepositSendCooldown = "deposit.send_cooldown";

  public const string ImageInvalid = "image.invalid";
  public const string ImageTooLarge = "image.too_large";
  public const string ImageUnsupportedFormat = "image.unsupported_format";

  public const string SmsTemplateTypeNotCustomizable = "sms_template.type_not_customizable";
  public const string SmsTemplateBodyEmpty = "sms_template.body_empty";
  public const string SmsTemplateTooLong = "sms_template.too_long";
  public const string SmsTemplateInvalidPlaceholder = "sms_template.invalid_placeholder";
  public const string SmsTemplateNotPending = "sms_template.not_pending";
}
