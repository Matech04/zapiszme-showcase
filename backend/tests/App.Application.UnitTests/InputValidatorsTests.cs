using App.Application.Appointments.Commands.UpdateAppointmentStatus;
using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Common.Validation;
using App.Application.Customers.Commands.CreateCustomer;
using App.Application.Notifications.Queries.GetOutsideScheduleAppointments;
using App.Application.Customers.Commands.UpdateCustomer;
using App.Application.Employees.Commands.AddEmployeeLeave;
using App.Application.Employees.Commands.UpdateEmployee;
using App.Application.SalonSettings.Commands.UpdateCurrentSalonSettings;
using App.Application.ServiceCategories.Commands.CreateServiceCategory;
using App.Application.Services.Commands.CreateService;
using App.Application.Tenants.Commands.CreateTenant;
using App.Application.VatRates.Commands.CreateVatRate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Application.UnitTests;

public sealed class InputValidatorsTests
{
  [Fact]
  public void Create_customer_rejects_invalid_email()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("Jan", "Kowalski", "not-an-email", "+48501110001", "");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerCommand.Email));
  }

  [Fact]
  public void Create_customer_allows_missing_email_and_phone()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("Jan", "Kowalski", "", "", "");

    var result = validator.Validate(command);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void Update_customer_allows_missing_email_and_phone()
  {
    var validator = new UpdateCustomerCommandValidator();
    var command = new UpdateCustomerCommand(Guid.NewGuid(), "Jan", "Kowalski", "", "", "");

    var result = validator.Validate(command);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void Create_customer_allows_missing_names_with_phone_only()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("", "", "", "+48501110001", "");

    Assert.True(validator.Validate(command).IsValid);
  }

  [Fact]
  public void Create_customer_allows_missing_names_with_email_only()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("", "", "jan@e.co", "", "");

    Assert.True(validator.Validate(command).IsValid);
  }

  [Fact]
  public void Create_customer_rejects_when_no_identifier_at_all()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("", "", "", "", "");

    Assert.False(validator.Validate(command).IsValid);
  }

  [Fact]
  public void Update_customer_allows_missing_names_with_phone_only()
  {
    var validator = new UpdateCustomerCommandValidator();
    var command = new UpdateCustomerCommand(Guid.NewGuid(), "", "", "", "+48501110001", "");

    Assert.True(validator.Validate(command).IsValid);
  }

  [Fact]
  public void Update_customer_rejects_when_no_identifier_at_all()
  {
    var validator = new UpdateCustomerCommandValidator();
    var command = new UpdateCustomerCommand(Guid.NewGuid(), "", "", "", "", "");

    Assert.False(validator.Validate(command).IsValid);
  }

  [Fact]
  public void Create_customer_rejects_invalid_phone_when_provided()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand("Jan", "Kowalski", "", "not-a-phone", "");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerCommand.PhoneNumber));
  }

  [Fact]
  public void Appointment_notes_rejects_oversized_text()
  {
    var validator = new UpdateAppointmentNotesCommandValidator();
    var command = new UpdateAppointmentNotesCommand(Guid.NewGuid(), new string('x', ValidationLimits.Notes + 1));

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAppointmentNotesCommand.newNotes));
  }

  [Fact]
  public void Swap_appointments_rejects_same_appointment()
  {
    var validator = new SwapAppointmentsCommandValidator();
    var id = Guid.NewGuid();
    var command = new App.Application.Appointments.Commands.SwapAppointments.SwapAppointmentsCommand(id, id);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.SecondAppointmentId));
  }

  [Fact]
  public void Swap_appointments_rejects_empty_ids()
  {
    var validator = new SwapAppointmentsCommandValidator();
    var command = new App.Application.Appointments.Commands.SwapAppointments.SwapAppointmentsCommand(Guid.Empty, Guid.NewGuid());

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.FirstAppointmentId));
  }

  [Fact]
  public void Verify_otp_rejects_non_six_digit_code()
  {
    var validator = new VerifyOtpCommandValidator();
    var command = new VerifyOtpCommand(Guid.NewGuid(), Guid.NewGuid(), "abc123");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(VerifyOtpCommand.Otp));
  }

  [Fact]
  public void Create_service_allows_zero_duration_for_addons()
  {
    // Usługa-dodatek (combo) może trwać 0 min — nie blokuje czasu w kalendarzu.
    var validator = new CreateServiceCommandValidator();
    var command = new CreateServiceCommand(Guid.NewGuid(), Guid.NewGuid(), "French", 20m, "PLN", 0);

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateServiceCommand.DurationInMinutes));
  }

  [Fact]
  public void Create_service_rejects_negative_duration()
  {
    var validator = new CreateServiceCommandValidator();
    var command = new CreateServiceCommand(Guid.NewGuid(), Guid.NewGuid(), "Strzyzenie", 100m, "PLN", -1);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceCommand.DurationInMinutes));
  }

  // ── REQ-013/014: Customer firstName/lastName max 100 ───────────────────────────────────

  [Fact]
  public void Create_customer_rejects_first_name_longer_than_100()
  {
    var validator = new CreateCustomerCommandValidator();
    var command = new CreateCustomerCommand(new string('x', 101), "Kowalski", "jan@e.co", "+48501110001", "");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerCommand.FirstName));
  }

  // ── REQ-015 PARTIAL: Customer email max 254 (BE) ────────────────────────────────────────

  [Fact]
  public void Create_customer_rejects_email_longer_than_254()
  {
    var validator = new CreateCustomerCommandValidator();
    var longEmail = new string('a', 250) + "@e.co"; // 255 chars
    var command = new CreateCustomerCommand("Jan", "Kowalski", longEmail, "+48501110001", "");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCustomerCommand.Email));
  }

  // ── REQ-017 PARTIAL: Customer generalNotes max 1000 ────────────────────────────────────

  [Fact]
  public void Update_customer_rejects_general_notes_longer_than_1000()
  {
    var validator = new UpdateCustomerCommandValidator();
    var notes = new string('n', ValidationLimits.Notes + 1);
    var command = new UpdateCustomerCommand(Guid.NewGuid(), "Jan", "K", "jan@e.co", "+48501110001", notes);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCustomerCommand.GeneralNotes));
  }

  // ── REQ-024 MISSING: UpdateEmployee Specialization nie ma MaxLength — bug exposed ──────

  [Fact]
  public void Update_employee_validator_does_not_reject_long_specialization_documents_gap()
  {
    // KRYTYCZNE: brak MaxLength dla Specialization w UpdateEmployeeCommandValidator (REQ-024).
    // Ten test dokumentuje lukę — 201 znaków przechodzi, ale wg matrycy powinno być MaxLength(200).
    var validator = new UpdateEmployeeCommandValidator();
    var longSpec = new string('s', 201);
    var command = new UpdateEmployeeCommand(Guid.NewGuid(), "Jan", "K", longSpec);

    var result = validator.Validate(command);

    Assert.True(result.IsValid,
      "Specialization 201 znaków powinna być odrzucana wg REQ-024, aktualnie przechodzi — brak walidacji MaxLength");
  }

  // ── REQ-038 PARTIAL: Tenant settings salonName max 100 (BE) ────────────────────────────

  [Fact]
  public void Update_current_salon_settings_rejects_name_longer_than_100()
  {
    var validator = new UpdateCurrentSalonSettingsCommandValidator();
    var longName = new string('x', 101);
    var command = new UpdateCurrentSalonSettingsCommand(
      longName, "valid-slug", CustomerVerificationChannel.Email,
      15, "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCurrentSalonSettingsCommand.Name));
  }

  // ── Regulamin (TermsOfService): opcjonalny, max 5000 znaków ────────────────────────────

  [Fact]
  public void Update_current_salon_settings_rejects_terms_of_service_longer_than_5000()
  {
    var validator = new UpdateCurrentSalonSettingsCommandValidator();
    var longTerms = new string('x', 5001);
    var command = new UpdateCurrentSalonSettingsCommand(
      "Salon", "valid-slug", CustomerVerificationChannel.Email,
      15, "Europe/Warsaw", "PLN", TermsOfService: longTerms);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCurrentSalonSettingsCommand.TermsOfService));
  }

  [Fact]
  public void Update_current_salon_settings_accepts_terms_of_service_at_limit()
  {
    var validator = new UpdateCurrentSalonSettingsCommandValidator();
    var terms = new string('x', 5000);
    var command = new UpdateCurrentSalonSettingsCommand(
      "Salon", "valid-slug", CustomerVerificationChannel.Email,
      15, "Europe/Warsaw", "PLN", TermsOfService: terms);

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateCurrentSalonSettingsCommand.TermsOfService));
  }

  // ── REQ-040: appointmentSlotStepMinutes ∈ [1, 240] ─────────────────────────────────────

  [Theory]
  [InlineData(1)]
  [InlineData(5)]
  [InlineData(7)]
  [InlineData(10)]
  [InlineData(15)]
  [InlineData(20)]
  [InlineData(30)]
  [InlineData(45)]
  [InlineData(60)]
  [InlineData(240)]
  public void Update_salon_settings_accepts_allowed_slot_step(int step)
  {
    var validator = new UpdateCurrentSalonSettingsCommandValidator();
    var command = new UpdateCurrentSalonSettingsCommand(
      "Salon", "salon", CustomerVerificationChannel.Email,
      step, "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateCurrentSalonSettingsCommand.AppointmentSlotStepMinutes));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(241)]
  public void Update_salon_settings_rejects_disallowed_slot_step(int step)
  {
    var validator = new UpdateCurrentSalonSettingsCommandValidator();
    var command = new UpdateCurrentSalonSettingsCommand(
      "Salon", "salon", CustomerVerificationChannel.Email,
      step, "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCurrentSalonSettingsCommand.AppointmentSlotStepMinutes));
  }

  // ── REQ-072 / REQ-039: Tenant slug pattern ─────────────────────────────────────────────

  [Theory]
  [InlineData("invalid slug")]
  [InlineData("special!chars")]
  [InlineData("slash/in/slug")]
  public void Create_tenant_rejects_invalid_slug_pattern(string slug)
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", slug, "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.Slug));
  }

  [Theory]
  [InlineData("valid-slug")]
  [InlineData("salon123")]
  [InlineData("a-b-c-d")]
  public void Create_tenant_accepts_valid_slug_pattern(string slug)
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", slug, "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.Slug));
  }

  // ── REQ-073/009: timeZoneId musi być prawidłowy ────────────────────────────────────────

  [Fact]
  public void Create_tenant_rejects_invalid_time_zone()
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", "salon", "Mars/Olympus_Mons", "PLN");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.TimeZoneId));
  }

  [Fact]
  public void Create_tenant_accepts_valid_time_zone()
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", "salon", "Europe/Warsaw", "PLN");

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.TimeZoneId));
  }

  // ── REQ-074/010: currency 3-letter ISO ─────────────────────────────────────────────────

  [Theory]
  [InlineData("PLN")]
  [InlineData("EUR")]
  [InlineData("USD")]
  public void Create_tenant_accepts_iso_currency(string currency)
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", "salon", "Europe/Warsaw", currency);

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.Currency));
  }

  [Theory]
  [InlineData("PL")]    // too short
  [InlineData("PLNZ")]  // too long
  [InlineData("PL1")]   // contains digit (IsIsoCurrency requires letters only)
  public void Create_tenant_rejects_invalid_currency(string currency)
  {
    var validator = new CreateTenantCommandValidator();
    var command = new CreateTenantCommand("Salon", "salon", "Europe/Warsaw", currency);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTenantCommand.Currency));
  }

  // ── REQ-026/027/028: Service amount/currency/duration ──────────────────────────────────

  [Fact]
  public void Create_service_rejects_negative_amount()
  {
    var validator = new CreateServiceCommandValidator();
    var command = new CreateServiceCommand(Guid.NewGuid(), Guid.NewGuid(), "Cut", -1m, "PLN", 30);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceCommand.Amount));
  }

  [Theory]
  [InlineData("EU")]
  [InlineData("EURO")]
  public void Create_service_rejects_non_3_letter_currency(string currency)
  {
    var validator = new CreateServiceCommandValidator();
    var command = new CreateServiceCommand(Guid.NewGuid(), Guid.NewGuid(), "Cut", 50m, currency, 30);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceCommand.Currency));
  }

  [Fact]
  public void Create_service_rejects_duration_exceeding_24h()
  {
    var validator = new CreateServiceCommandValidator();
    var command = new CreateServiceCommand(Guid.NewGuid(), Guid.NewGuid(), "Cut", 50m, "PLN", 1441);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceCommand.DurationInMinutes));
  }

  // ── REQ-037: VatRate value [0, 1] ──────────────────────────────────────────────────────

  [Theory]
  [InlineData(0.0)]
  [InlineData(0.23)]
  [InlineData(1.0)]
  public void Create_vat_rate_accepts_value_in_range(decimal value)
  {
    var validator = new CreateVatRateCommandValidator();
    var command = new CreateVatRateCommand("VAT", value, false);

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateVatRateCommand.Value));
  }

  [Theory]
  [InlineData(-0.01)]
  [InlineData(1.01)]
  [InlineData(2)]
  public void Create_vat_rate_rejects_value_out_of_range(decimal value)
  {
    var validator = new CreateVatRateCommandValidator();
    var command = new CreateVatRateCommand("VAT", value, false);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVatRateCommand.Value));
  }

  // ── REQ-032: ServiceCategory orderIndex >= 0 ───────────────────────────────────────────

  [Fact]
  public void Create_service_category_rejects_negative_order_index()
  {
    var validator = new CreateServiceCategoryCommandValidator();
    var command = new CreateServiceCategoryCommand("Cat", -1);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceCategoryCommand.OrderIndex));
  }

  // ── REQ-051: AddLeave endDate >= startDate ─────────────────────────────────────────────

  [Fact]
  public void Add_employee_leave_rejects_end_before_start()
  {
    var validator = new AddEmployeeLeaveCommandValidator();
    var command = new AddEmployeeLeaveCommand(
      Guid.NewGuid(),
      new DateOnly(2026, 8, 10),
      new DateOnly(2026, 8, 1),
      AbsenceType.Vacation);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddEmployeeLeaveCommand.EndDate));
  }

  // ── REQ-066: VerifyOtp 6-digit regex (booking) ─────────────────────────────────────────

  [Theory]
  [InlineData("12345")]   // too short
  [InlineData("1234567")] // too long
  [InlineData("abcdef")]  // not digits
  [InlineData("")]        // empty
  public void Verify_booking_otp_rejects_non_six_digit(string otp)
  {
    var validator = new VerifyOtpCommandValidator();
    var command = new VerifyOtpCommand(Guid.NewGuid(), Guid.NewGuid(), otp);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(VerifyOtpCommand.Otp));
  }

  [Fact]
  public void Verify_booking_otp_accepts_six_digit()
  {
    var validator = new VerifyOtpCommandValidator();
    var command = new VerifyOtpCommand(Guid.NewGuid(), Guid.NewGuid(), "123456");

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(VerifyOtpCommand.Otp));
  }

  [Theory]
  [InlineData("ala<script>")] // znak <
  [InlineData("ala\"bama")]   // cudzysłów
  [InlineData("ala bama")]    // spacja
  [InlineData("ala/bama")]    // slash
  [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaX")] // 31 znaków > 30
  public void Verify_booking_otp_rejects_invalid_instagram_nick(string nick)
  {
    var validator = new VerifyOtpCommandValidator();
    var command = new VerifyOtpCommand(Guid.NewGuid(), Guid.NewGuid(), "123456", InstagramNick: nick);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(VerifyOtpCommand.InstagramNick));
  }

  [Theory]
  [InlineData("ala.bama_01")]
  [InlineData("nick")]
  [InlineData("@nick")] // wiodące @ dozwolone — domena je usuwa przy zapisie
  [InlineData(null)] // opcjonalny — brak walidacji
  [InlineData("")]   // pusty — opcjonalny
  public void Verify_booking_otp_accepts_valid_or_empty_instagram_nick(string? nick)
  {
    var validator = new VerifyOtpCommandValidator();
    var command = new VerifyOtpCommand(Guid.NewGuid(), Guid.NewGuid(), "123456", InstagramNick: nick);

    var result = validator.Validate(command);

    Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(VerifyOtpCommand.InstagramNick));
  }

  // ── confirm-with-session: parytet walidacji z verify-otp (imię/nazwisko/IG + NotEmpty tokeny) ──

  [Theory]
  [InlineData("ala<script>")]
  [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaX")] // >100
  public void Confirm_with_session_rejects_invalid_first_name(string firstName)
  {
    var validator = new ConfirmBookingWithSessionCommandValidator();
    var command = new ConfirmBookingWithSessionCommand("salon", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FirstName: firstName);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(ConfirmBookingWithSessionCommand.FirstName));
  }

  [Fact]
  public void Confirm_with_session_rejects_invalid_instagram_nick()
  {
    var validator = new ConfirmBookingWithSessionCommandValidator();
    var command = new ConfirmBookingWithSessionCommand("salon", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InstagramNick: "ala<script>");

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(ConfirmBookingWithSessionCommand.InstagramNick));
  }

  [Fact]
  public void Confirm_with_session_rejects_empty_tokens()
  {
    var validator = new ConfirmBookingWithSessionCommandValidator();
    var command = new ConfirmBookingWithSessionCommand("", Guid.Empty, Guid.Empty, Guid.Empty);

    var result = validator.Validate(command);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(ConfirmBookingWithSessionCommand.Slug));
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(ConfirmBookingWithSessionCommand.SessionToken));
  }

  [Fact]
  public void Confirm_with_session_accepts_valid_input()
  {
    var validator = new ConfirmBookingWithSessionCommandValidator();
    var command = new ConfirmBookingWithSessionCommand(
        "salon", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        FirstName: "Anna", LastName: "Kowalska", InstagramNick: "@anna_nails");

    var result = validator.Validate(command);

    Assert.True(result.IsValid);
  }

  // ── REQ-068, REQ-070 MISSING: SelfService validators nie istnieją ─────────────────────
  // Brak walidatorów RequestSelfServiceOtpCommandValidator i VerifySelfServiceOtpCommandValidator.
  // Testy dokumentują tę lukę przez reflection — gdy brakujące walidatory zostaną dodane,
  // assertion trzeba odwrócić.

  [Fact]
  public void Self_service_request_otp_validator_does_not_exist_documents_missing_max_length()
  {
    var asm = typeof(App.Application.Booking.SelfService.Commands.RequestSelfServiceOtpCommand).Assembly;
    var validator = asm.GetType("App.Application.Booking.SelfService.Commands.RequestSelfServiceOtpCommandValidator");

    Assert.Null(validator);
  }

  [Fact]
  public void Self_service_verify_otp_validator_does_not_exist_documents_missing_regex()
  {
    var asm = typeof(App.Application.Booking.SelfService.Commands.VerifySelfServiceOtpCommand).Assembly;
    var validator = asm.GetType("App.Application.Booking.SelfService.Commands.VerifySelfServiceOtpCommandValidator");

    Assert.Null(validator);
  }

  // Dzwonek panelu poll'uje to zapytanie co kilka sekund — nieograniczony zakres ciągnął całą historię.
  [Fact]
  public void Outside_schedule_rejects_range_over_cap()
  {
    var validator = new GetOutsideScheduleAppointmentsQueryValidator();
    var query = new GetOutsideScheduleAppointmentsQuery(new DateOnly(2020, 1, 1), new DateOnly(2026, 1, 1));

    Assert.False(validator.Validate(query).IsValid);
  }

  [Fact]
  public void Outside_schedule_accepts_dashboard_poll_window()
  {
    var validator = new GetOutsideScheduleAppointmentsQueryValidator();
    // Panel pyta o dziś..+14 dni (notification-center.service.ts).
    var query = new GetOutsideScheduleAppointmentsQuery(new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 23));

    Assert.True(validator.Validate(query).IsValid);
  }

  [Fact]
  public void Outside_schedule_rejects_inverted_range()
  {
    var validator = new GetOutsideScheduleAppointmentsQueryValidator();
    var query = new GetOutsideScheduleAppointmentsQuery(new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 9));

    Assert.False(validator.Validate(query).IsValid);
  }
}
