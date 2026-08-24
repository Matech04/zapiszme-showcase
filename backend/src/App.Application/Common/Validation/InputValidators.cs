using App.Application.Appointments.Commands.CreateAppointment;
using App.Application.Appointments.Commands.ApplyReschedule;
using App.Application.Appointments.Commands.PlaceAppointment;
using App.Application.Appointments.Commands.DeleteAppointment;
using App.Application.Appointments.Commands.RescheduleAppointment;
using App.Application.Appointments.Commands.SetFinalPrice;
using App.Application.Appointments.Commands.UpdateAppointmentStatus;
using App.Application.Appointments.Queries.GetAppointmentById;
using App.Application.Appointments.Queries.GetAppointmentMonthAvailability;
using App.Application.Appointments.Queries.GetAppointmentsByRange;
using App.Application.Appointments.Queries.GetAvailableTimeSlots;
using App.Application.Appointments.Queries.GetCustomerAppointments;
using App.Application.Booking.BookingAppointments.Commands;
using App.Application.Booking.BookingAppointments.Queries;
using App.Application.Notifications.Queries.GetOutsideScheduleAppointments;
using App.Application.Booking.BookingEmployees.Queries;
using App.Application.Booking.BookingSalon.Queries;
using App.Application.Booking.BookingServices.Queries;
using App.Application.Customers.Commands.CreateCustomer;
using App.Application.Customers.Commands.DeleteCustomer;
using App.Application.Customers.Commands.UpdateCustomer;
using App.Application.Customers.Commands.UpdateCustomerNotes;
using App.Application.Customers.Queries.GetCustomerById;
using App.Application.Customers.Queries.SearchCustomersQuery;
using App.Application.Employees.Commands.AddEmployeeLeave;
using App.Application.Employees.Commands.DeleteEmployeeSchedule;
using App.Application.Employees.Commands.AssignService;
using App.Application.Employees.Commands.CreateEmployee;
using App.Application.Employees.Commands.DeleteEmployee;
using App.Application.Employees.Commands.RemoveEmployeeLeave;
using App.Application.Employees.Commands.RemoveMonthPublication;
using App.Application.Employees.Commands.RemoveScheduleOverride;
using App.Application.Employees.Commands.SetMonthPublication;
using App.Application.Employees.Commands.SetScheduleOverride;
using App.Application.Employees.Commands.SetEmployeeSchedule;
using App.Application.Employees.Commands.UnassignService;
using App.Application.Employees.Commands.UpdateEmployee;
using App.Application.Employees.Commands.UpdateEmployeeService;
using App.Application.Employees.Dtos;
using App.Application.Employees.Queries.GetEmployeeById;
using App.Application.Employees.Queries.GetEmployeeLeaves;
using App.Application.Employees.Queries.GetEmployeeServices;
using App.Application.Employees.Queries.GetScheduleOverrides;
using App.Application.Employees.Queries.GetEmployeeSchedules;
using App.Application.SalonSettings.Commands.UpdateCurrentSalonSettings;
using App.Application.ServiceCategories.Commands.CreateServiceCategory;
using App.Application.ServiceCategories.Commands.DeleteServiceCategory;
using App.Application.ServiceCategories.Commands.UpdateServiceCategory;
using App.Application.ServiceCategories.Queries.GetServiceCategoryById;
using App.Application.Services.Commands.CreateService;
using App.Application.Services.Commands.DeleteService;
using App.Application.Services.Commands.UpdateService;
using App.Application.Services.Queries.GetServiceById;
using App.Application.ShiftTemplates.Commands.CreateShiftTemplate;
using App.Application.ShiftTemplates.Commands.DeleteShiftTemplate;
using App.Application.ShiftTemplates.Commands.UpdateShiftTemplate;
using App.Application.ShiftTemplates.Queries.GetShiftTemplateById;
using App.Application.ShiftTemplates.Queries.GetShiftTemplates;
using App.Application.Tenants.Commands.CreateTenant;
using App.Application.Tenants.Commands.DeleteTenant;
using App.Application.Tenants.Commands.UpdateTenant;
using App.Application.Tenants.Queries.GetTenantById;
using App.Application.Tenants.Queries.GetRegisterOwnerSlugAvailability;
using App.Application.VatRates.Commands.CreateVatRate;
using App.Application.VatRates.Commands.DeleteVatRate;
using App.Application.VatRates.Commands.UpdateVatRate;
using App.Application.VatRates.Queries.GetVatRateById;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using FluentValidation;

namespace App.Application.Common.Validation;

public static class ValidationLimits
{
  public const int ShortName = 50;
  public const int Name = 100;
  public const int LongName = 200;
  public const int Email = 254;
  public const int Phone = 30;
  public const int Notes = 1000;
  public const int Slug = 120;
  public const int SearchTerm = 100;
  public const int TermsOfService = 5000;
}

public abstract class AppValidator<T> : AbstractValidator<T>
{
  protected static bool NotEmptyIfPresent(Guid? value) => !value.HasValue || value.Value != Guid.Empty;

  /// <summary>Lista usług combo: 1..MaxServices, bez pustych Guidów i bez duplikatów (reguła grup sprawdzana w handlerze).</summary>
  protected static bool IsValidServiceIdList(IReadOnlyList<Guid>? ids) =>
    ids is { Count: > 0 }
    && ids.Count <= App.Domain.Aggregates.AppointmentAggregate.Appointment.MaxServices
    && ids.All(id => id != Guid.Empty)
    && ids.Distinct().Count() == ids.Count;

  protected static bool NotDefaultDate(DateOnly value) => value != default;

  protected static bool IsValidPhoneNumber(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    try
    {
      _ = new PhoneNumber(value);
      return true;
    }
    catch
    {
      return false;
    }
  }

  protected static bool IsSlug(string value)
  {
    return value.All(c => char.IsLetterOrDigit(c) || c == '-');
  }

  protected static bool IsIsoCurrency(string value)
  {
    return value.Length == 3 && value.All(char.IsLetter);
  }

  protected static bool IsValidTimeZoneId(string value)
  {
    try
    {
      _ = TimeZoneInfo.FindSystemTimeZoneById(value);
      return true;
    }
    catch
    {
      return false;
    }
  }
}

public sealed class CreateTenantCommandValidator : AppValidator<CreateTenantCommand>
{
  public CreateTenantCommandValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Slug)
        .NotEmpty()
        .MaximumLength(ValidationLimits.Slug)
        .Must(IsSlug)
        .WithMessage("Slug can contain only letters, digits and hyphens.");
    RuleFor(x => x.TimeZoneId).NotEmpty().Must(IsValidTimeZoneId);
    RuleFor(x => x.Currency).NotEmpty().Must(IsIsoCurrency);
  }
}

public sealed class UpdateTenantCommandValidator : AppValidator<UpdateTenantCommand>
{
  public UpdateTenantCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Slug)
        .NotEmpty()
        .MaximumLength(ValidationLimits.Slug)
        .Must(IsSlug)
        .WithMessage("Slug can contain only letters, digits and hyphens.");
    RuleFor(x => x.TimeZoneId).NotEmpty().Must(IsValidTimeZoneId);
    RuleFor(x => x.Currency).NotEmpty().Must(IsIsoCurrency);
  }
}

public sealed class DeleteTenantCommandValidator : AppValidator<DeleteTenantCommand>
{
  public DeleteTenantCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetTenantByIdQueryValidator : AppValidator<GetTenantByIdQuery>
{
  public GetTenantByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateCurrentSalonSettingsCommandValidator : AppValidator<UpdateCurrentSalonSettingsCommand>
{
  public UpdateCurrentSalonSettingsCommandValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Slug)
        .NotEmpty()
        .MaximumLength(ValidationLimits.Slug)
        .Must(IsSlug)
        .WithMessage("Slug can contain only letters, digits and hyphens.");
    RuleFor(x => x.AppointmentSlotStepMinutes)
        .InclusiveBetween(1, 240)
        .WithMessage("Interwał slotów musi być między 1 a 240 minut.");
    // Zakres lustrzany wobec Guard.AgainstInvalidBookingHorizonDays — walidator daje czytelne 400
    // zamiast ArgumentException z agregatu. Null = nie zmieniamy bieżącej wartości.
    RuleFor(x => x.BookingHorizonDays!.Value)
        .InclusiveBetween(1, 1826)
        .WithMessage("Horyzont rezerwacji musi być między 1 a 1826 dni.")
        .When(x => x.BookingHorizonDays.HasValue);
    RuleFor(x => x.CustomerVerificationChannel).IsInEnum();
    RuleFor(x => x.TimeZoneId).NotEmpty().Must(IsValidTimeZoneId);
    RuleFor(x => x.Currency).NotEmpty().Must(IsIsoCurrency);
    // Kolory kalendarza: pusty/brak = motyw domyślny; w przeciwnym razie hex #RRGGBB.
    const string hexPattern = "^#[0-9A-Fa-f]{6}$";
    const string hexMsg = "Kolor musi być w formacie #RRGGBB.";
    RuleFor(x => x.BookingCalendarColorHex).Matches(hexPattern).WithMessage(hexMsg)
        .When(x => !string.IsNullOrWhiteSpace(x.BookingCalendarColorHex));
    RuleFor(x => x.BookingCalendarBackgroundHex).Matches(hexPattern).WithMessage(hexMsg)
        .When(x => !string.IsNullOrWhiteSpace(x.BookingCalendarBackgroundHex));
    RuleFor(x => x.BookingCalendarSurfaceHex).Matches(hexPattern).WithMessage(hexMsg)
        .When(x => !string.IsNullOrWhiteSpace(x.BookingCalendarSurfaceHex));
    RuleFor(x => x.BookingCalendarPriceHex).Matches(hexPattern).WithMessage(hexMsg)
        .When(x => !string.IsNullOrWhiteSpace(x.BookingCalendarPriceHex));
    // Regulamin: opcjonalny, ograniczony długością przeciw nadużyciom (DoS / spam).
    RuleFor(x => x.TermsOfService)
        .MaximumLength(ValidationLimits.TermsOfService)
        .WithMessage($"Regulamin nie może przekraczać {ValidationLimits.TermsOfService} znaków.");
  }
}

public sealed class CreateCustomerCommandValidator : AppValidator<CreateCustomerCommand>
{
  public CreateCustomerCommandValidator()
  {
    RuleFor(x => x.FirstName).MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.LastName).MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Email).MaximumLength(ValidationLimits.Email).EmailAddress()
        .When(x => !string.IsNullOrWhiteSpace(x.Email));
    RuleFor(x => x.PhoneNumber).MaximumLength(ValidationLimits.Phone).Must(IsValidPhoneNumber)
        .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    RuleFor(x => x.GeneralNotes).MaximumLength(ValidationLimits.Notes);
    RuleFor(x => x.InstagramNick)
        .MaximumLength(30)
        .Matches(@"^@?[A-Za-z0-9._]+$").WithMessage("Nick na Instagramie zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.InstagramNick));
    RuleFor(x => x)
        .Must(HasAnyIdentifier)
        .WithMessage("Podaj imię, nazwisko, telefon lub e-mail.");
  }

  // Klient potrzebuje przynajmniej jednego identyfikatora — pusty rekord jest bezużyteczny.
  private static bool HasAnyIdentifier(CreateCustomerCommand x) =>
      !string.IsNullOrWhiteSpace(x.FirstName)
      || !string.IsNullOrWhiteSpace(x.LastName)
      || !string.IsNullOrWhiteSpace(x.Email)
      || !string.IsNullOrWhiteSpace(x.PhoneNumber);
}

public sealed class UpdateCustomerCommandValidator : AppValidator<UpdateCustomerCommand>
{
  public UpdateCustomerCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.FirstName).MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.LastName).MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Email).MaximumLength(ValidationLimits.Email).EmailAddress()
        .When(x => !string.IsNullOrWhiteSpace(x.Email));
    RuleFor(x => x.PhoneNumber).MaximumLength(ValidationLimits.Phone).Must(IsValidPhoneNumber)
        .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    RuleFor(x => x.GeneralNotes).MaximumLength(ValidationLimits.Notes);
    RuleFor(x => x.InstagramNick)
        .MaximumLength(30)
        .Matches(@"^@?[A-Za-z0-9._]+$").WithMessage("Nick na Instagramie zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.InstagramNick));
    RuleFor(x => x)
        .Must(HasAnyIdentifier)
        .WithMessage("Podaj imię, nazwisko, telefon lub e-mail.");
  }

  // Klient potrzebuje przynajmniej jednego identyfikatora — pusty rekord jest bezużyteczny.
  private static bool HasAnyIdentifier(UpdateCustomerCommand x) =>
      !string.IsNullOrWhiteSpace(x.FirstName)
      || !string.IsNullOrWhiteSpace(x.LastName)
      || !string.IsNullOrWhiteSpace(x.Email)
      || !string.IsNullOrWhiteSpace(x.PhoneNumber);
}

public sealed class UpdateCustomerNotesCommandValidator : AppValidator<UpdateCustomerNotesCommand>
{
  public UpdateCustomerNotesCommandValidator()
  {
    RuleFor(x => x.CustomerId).NotEmpty();
    RuleFor(x => x.newNotes).MaximumLength(ValidationLimits.Notes);
  }
}

public sealed class DeleteCustomerCommandValidator : AppValidator<DeleteCustomerCommand>
{
  public DeleteCustomerCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetCustomerByIdQueryValidator : AppValidator<GetCustomerByIdQuery>
{
  public GetCustomerByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SearchCustomersQueryValidator : AppValidator<SearchCustomersQuery>
{
  public SearchCustomersQueryValidator() => RuleFor(x => x.searchTerm).MaximumLength(ValidationLimits.SearchTerm);
}

public sealed class CreateEmployeeCommandValidator : AppValidator<CreateEmployeeCommand>
{
  public CreateEmployeeCommandValidator()
  {
    RuleFor(x => x.UserId).Must(NotEmptyIfPresent);
    RuleFor(x => x.FirstName).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.LastName).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.Email).NotEmpty().MaximumLength(ValidationLimits.Name).EmailAddress();
  }
}

public sealed class UpdateEmployeeCommandValidator : AppValidator<UpdateEmployeeCommand>
{
  public UpdateEmployeeCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.FirstName).NotEmpty().MaximumLength(ValidationLimits.Name);
    RuleFor(x => x.LastName).NotEmpty().MaximumLength(ValidationLimits.Name);
  }
}

public sealed class DeleteEmployeeCommandValidator : AppValidator<DeleteEmployeeCommand>
{
  public DeleteEmployeeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetEmployeeByIdQueryValidator : AppValidator<GetEmployeeByIdQuery>
{
  public GetEmployeeByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class EmployeeIdQueryValidator : AppValidator<GetEmployeeServicesQuery>
{
  public EmployeeIdQueryValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class GetEmployeeLeavesQueryValidator : AppValidator<GetEmployeeLeavesQuery>
{
  public GetEmployeeLeavesQueryValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class GetScheduleOverridesQueryValidator : AppValidator<GetScheduleOverridesQuery>
{
  public GetScheduleOverridesQueryValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class GetEmployeeSchedulesQueryValidator : AppValidator<GetEmployeeSchedulesQuery>
{
  public GetEmployeeSchedulesQueryValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

public sealed class AssignServiceCommandValidator : AppValidator<AssignServiceCommand>
{
  public AssignServiceCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceId).NotEmpty();
    // Wartości custom są opcjonalne (null = dziedzicz z katalogu); waliduj tylko gdy podane.
    RuleFor(x => x.CustomDuration!.Value).GreaterThan(0).LessThanOrEqualTo(24 * 60).When(x => x.CustomDuration.HasValue);
    RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue);
    RuleFor(x => x.Currency).NotEmpty().Length(3).When(x => x.Amount.HasValue);
  }
}

public sealed class UpdateEmployeeServiceCommandValidator : AppValidator<UpdateEmployeeServiceCommand>
{
  public UpdateEmployeeServiceCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceId).NotEmpty();
    RuleFor(x => x.CustomDuration!.Value).GreaterThan(0).LessThanOrEqualTo(24 * 60).When(x => x.CustomDuration.HasValue);
    RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0).When(x => x.Amount.HasValue);
    RuleFor(x => x.Currency).NotEmpty().Length(3).When(x => x.Amount.HasValue);
  }
}

public sealed class UnassignServiceCommandValidator : AppValidator<UnassignServiceCommand>
{
  public UnassignServiceCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceId).NotEmpty();
  }
}

public sealed class AddEmployeeLeaveCommandValidator : AppValidator<AddEmployeeLeaveCommand>
{
  public AddEmployeeLeaveCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.StartDate).Must(NotDefaultDate);
    RuleFor(x => x.EndDate).Must(NotDefaultDate).GreaterThanOrEqualTo(x => x.StartDate);
    RuleFor(x => x.AbsenceType).IsInEnum();
  }
}

public sealed class RemoveEmployeeLeaveCommandValidator : AppValidator<RemoveEmployeeLeaveCommand>
{
  public RemoveEmployeeLeaveCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.LeaveId).NotEmpty();
  }
}

public sealed class RemoveScheduleOverrideCommandValidator : AppValidator<RemoveScheduleOverrideCommand>
{
  public RemoveScheduleOverrideCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class TimeRangeDtoValidator : AppValidator<TimeRangeDto>
{
  public TimeRangeDtoValidator()
  {
    RuleFor(x => x).Must(x => x.StartTime < x.EndTime).WithMessage("StartTime must be before EndTime.");
  }
}

public sealed class ScheduleOverrideDtoValidator : AppValidator<ScheduleOverrideDto>
{
  public ScheduleOverrideDtoValidator()
  {
    RuleFor(x => x.Date).Must(NotDefaultDate);
    RuleForEach(x => x.WorkRanges).SetValidator(new TimeRangeDtoValidator()).When(x => x.WorkRanges is not null);
    RuleForEach(x => x.Breaks).SetValidator(new TimeRangeDtoValidator()).When(x => x.Breaks is not null);
  }
}

public sealed class SetScheduleOverrideCommandValidator : AppValidator<SetScheduleOverrideCommand>
{
  public SetScheduleOverrideCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.Date).Must(NotDefaultDate);
    RuleForEach(x => x.WorkRanges).SetValidator(new TimeRangeDtoValidator()).When(x => x.WorkRanges is not null);
    RuleForEach(x => x.Breaks).SetValidator(new TimeRangeDtoValidator()).When(x => x.Breaks is not null);
  }
}

public sealed class SetMonthPublicationCommandValidator : AppValidator<SetMonthPublicationCommand>
{
  public SetMonthPublicationCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.Year).InclusiveBetween(2000, 9999);
    RuleFor(x => x.Month).InclusiveBetween(1, 12);
    // OpensOn == null jest poprawne (zamknięcie bezterminowe), ale domyślna data to pomyłka.
    RuleFor(x => x.OpensOn!.Value).Must(NotDefaultDate).When(x => x.OpensOn.HasValue);
  }
}

public sealed class RemoveMonthPublicationCommandValidator : AppValidator<RemoveMonthPublicationCommand>
{
  public RemoveMonthPublicationCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.Year).InclusiveBetween(2000, 9999);
    RuleFor(x => x.Month).InclusiveBetween(1, 12);
  }
}

// internal — walidator-dziecko używany tylko ręcznie przez SetValidator(...); ma parametr w
// konstruktorze (tryb), więc NIE może trafić do auto-rejestracji DI (AddValidatorsFromAssembly
// pomija typy internal). Jako public wywaliłby się na ValidateOnBuild (Development) — ctor nie do rozwiązania.
internal sealed class EmployeeScheduleDayDtoValidator : AppValidator<EmployeeScheduleDayDto>
{
  // Reguły zależą od trybu generowania slotów: w trybie stałych slotów dzień
  // ma SAME godziny startu (FixedStartTimes), a WorkRanges/Breaks są puste — i odwrotnie.
  public EmployeeScheduleDayDtoValidator(SlotGenerationMode mode)
  {
    RuleFor(x => x.CycleIndex).GreaterThanOrEqualTo(0);

    if (mode == SlotGenerationMode.FixedStartTimes)
    {
      RuleFor(x => x.FixedStartTimes)
        .NotNull()
        .Must(t => t!.Count > 0)
        .WithMessage("At least one fixed start time is required.");
    }
    else
    {
      RuleFor(x => x.WorkRanges).NotNull().Must(r => r.Count > 0).WithMessage("At least one work range is required.");
      RuleForEach(x => x.WorkRanges).SetValidator(new TimeRangeDtoValidator());
      RuleFor(x => x.Breaks).NotNull();
      RuleForEach(x => x.Breaks).SetValidator(new TimeRangeDtoValidator());
    }
  }
}

public sealed class EmployeeScheduleDtoValidator : AppValidator<EmployeeScheduleDto>
{
  public EmployeeScheduleDtoValidator()
  {
    RuleFor(x => x.ActiveFrom).Must(NotDefaultDate);
    RuleFor(x => x.ActiveTo).Must(NotDefaultDate).GreaterThanOrEqualTo(x => x.ActiveFrom);
    RuleFor(x => x.NumberOfCycles).InclusiveBetween(1, 4);
    RuleFor(x => x.Days).NotNull();
    RuleForEach(x => x.Days).SetValidator(schedule => new EmployeeScheduleDayDtoValidator(schedule.SlotGenerationMode));
  }
}

public sealed class SetEmployeeScheduleCommandValidator : AppValidator<SetEmployeeScheduleCommand>
{
  public SetEmployeeScheduleCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.Schedule).NotNull().SetValidator(new EmployeeScheduleDtoValidator());
  }
}

public sealed class DeleteEmployeeScheduleCommandValidator : AppValidator<DeleteEmployeeScheduleCommand>
{
  public DeleteEmployeeScheduleCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ScheduleId).NotEmpty();
  }
}

public sealed class CreateServiceCommandValidator : AppValidator<CreateServiceCommand>
{
  public CreateServiceCommandValidator()
  {
    // CategoryId opcjonalne (Opcja A): null = usługa bez kategorii (orphan).
    // Gdy podane, musi być niepusty Guid — istnienie w tenancie sprawdza handler.
    RuleFor(x => x.CategoryId).Must(NotEmptyIfPresent);
    RuleFor(x => x.VatRateId).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.LongName);
    RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    RuleFor(x => x.Currency).NotEmpty().Length(3);
    // 0 dozwolone: usługi-dodatki (combo) nie blokują czasu w kalendarzu.
    RuleFor(x => x.DurationInMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);

    // Widełki cen + przedział czasu (opcjonalne, null = wartość stała).
    RuleFor(x => x.MaxAmount)
        .GreaterThanOrEqualTo(x => x.Amount)
        .When(x => x.MaxAmount.HasValue)
        .WithMessage("Górna granica ceny nie może być mniejsza niż cena bazowa.");
    RuleFor(x => x.DurationMinMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60)
        .When(x => x.DurationMinMinutes.HasValue);
    RuleFor(x => x.DurationMaxMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60)
        .When(x => x.DurationMaxMinutes.HasValue);
    RuleFor(x => x.DurationMaxMinutes).GreaterThanOrEqualTo(x => x.DurationMinMinutes)
        .When(x => x.DurationMinMinutes.HasValue && x.DurationMaxMinutes.HasValue)
        .WithMessage("Górna granica czasu nie może być mniejsza niż dolna.");
    RuleFor(x => x).Must(x => x.DurationMinMinutes.HasValue == x.DurationMaxMinutes.HasValue)
        .WithMessage("Przedział czasu wymaga obu granic (min i max).");

    // Media usługi (#5): opis + galeria zdjęć. Front uploaduje pliki wcześniej (POST /api/media/images)
    // i podaje gotowe URL-e + klucz storage — tu pilnujemy tylko kontraktu komendy.
    RuleFor(x => x.Description)
        .MaximumLength(Service.MaxDescriptionLength)
        .When(x => x.Description is not null)
        .WithMessage($"Opis może mieć maksymalnie {Service.MaxDescriptionLength} znaków.");
    RuleFor(x => x.Images!)
        .Must(images => images.Count <= Service.MaxImages)
        .When(x => x.Images is not null)
        .WithMessage($"Usługa może mieć maksymalnie {Service.MaxImages} zdjęć.");
    RuleForEach(x => x.Images!).ChildRules(img =>
    {
      img.RuleFor(i => i.Url).NotEmpty();
      img.RuleFor(i => i.ThumbnailUrl).NotEmpty();
      img.RuleFor(i => i.Key).NotEmpty();
    }).When(x => x.Images is not null);
  }
}

public sealed class UpdateServiceCommandValidator : AppValidator<UpdateServiceCommand>
{
  public UpdateServiceCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    // CategoryId opcjonalne: null = wyczyść kategorię na istniejącej usłudze.
    RuleFor(x => x.CategoryId).Must(NotEmptyIfPresent);
    RuleFor(x => x.VatRateId).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.LongName);
    RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    RuleFor(x => x.Currency).NotEmpty().Length(3);
    // 0 dozwolone: usługi-dodatki (combo) nie blokują czasu w kalendarzu.
    RuleFor(x => x.DurationInMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);

    RuleFor(x => x.MaxAmount)
        .GreaterThanOrEqualTo(x => x.Amount)
        .When(x => x.MaxAmount.HasValue)
        .WithMessage("Górna granica ceny nie może być mniejsza niż cena bazowa.");
    RuleFor(x => x.DurationMinMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60)
        .When(x => x.DurationMinMinutes.HasValue);
    RuleFor(x => x.DurationMaxMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60)
        .When(x => x.DurationMaxMinutes.HasValue);
    RuleFor(x => x.DurationMaxMinutes).GreaterThanOrEqualTo(x => x.DurationMinMinutes)
        .When(x => x.DurationMinMinutes.HasValue && x.DurationMaxMinutes.HasValue)
        .WithMessage("Górna granica czasu nie może być mniejsza niż dolna.");
    RuleFor(x => x).Must(x => x.DurationMinMinutes.HasValue == x.DurationMaxMinutes.HasValue)
        .WithMessage("Przedział czasu wymaga obu granic (min i max).");

    // Media usługi (#5): opis + galeria zdjęć (reconcile na edycji).
    RuleFor(x => x.Description)
        .MaximumLength(Service.MaxDescriptionLength)
        .When(x => x.Description is not null)
        .WithMessage($"Opis może mieć maksymalnie {Service.MaxDescriptionLength} znaków.");
    RuleFor(x => x.Images!)
        .Must(images => images.Count <= Service.MaxImages)
        .When(x => x.Images is not null)
        .WithMessage($"Usługa może mieć maksymalnie {Service.MaxImages} zdjęć.");
    RuleForEach(x => x.Images!).ChildRules(img =>
    {
      img.RuleFor(i => i.Url).NotEmpty();
      img.RuleFor(i => i.ThumbnailUrl).NotEmpty();
      img.RuleFor(i => i.Key).NotEmpty();
    }).When(x => x.Images is not null);
  }
}

public sealed class DeleteServiceCommandValidator : AppValidator<DeleteServiceCommand>
{
  public DeleteServiceCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class ReorderServicesCommandValidator : AppValidator<App.Application.Services.Commands.ReorderServices.ReorderServicesCommand>
{
  public ReorderServicesCommandValidator()
  {
    RuleFor(x => x.OrderedServiceIds)
      .NotNull()
      .Must(ids => ids.All(id => id != Guid.Empty)).WithMessage("Lista zawiera nieprawidłowy identyfikator usługi.")
      .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Lista zawiera zduplikowane identyfikatory.");
    RuleFor(x => x.CategoryId).Must(NotEmptyIfPresent);
  }
}

public sealed class ReorderServiceCategoriesCommandValidator
  : AppValidator<App.Application.ServiceCategories.Commands.ReorderServiceCategories.ReorderServiceCategoriesCommand>
{
  public ReorderServiceCategoriesCommandValidator()
  {
    RuleFor(x => x.OrderedCategoryIds)
      .NotNull()
      .Must(ids => ids.All(id => id != Guid.Empty)).WithMessage("Lista zawiera nieprawidłowy identyfikator kategorii.")
      .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Lista zawiera zduplikowane identyfikatory.");
  }
}

public sealed class GetServiceByIdQueryValidator : AppValidator<GetServiceByIdQuery>
{
  public GetServiceByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetShiftTemplatesQueryValidator : AppValidator<GetShiftTemplatesQuery>
{
  public GetShiftTemplatesQueryValidator() { }
}

public sealed class GetShiftTemplateByIdQueryValidator : AppValidator<GetShiftTemplateByIdQuery>
{
  public GetShiftTemplateByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CreateShiftTemplateCommandValidator : AppValidator<CreateShiftTemplateCommand>
{
  public CreateShiftTemplateCommandValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.Name);

    // Tryb stałych godzin: wymagane FixedStartTimes; tryb siatki: wymagane WorkRanges (+ przerwy).
    When(x => x.SlotGenerationMode == SlotGenerationMode.FixedStartTimes, () =>
    {
      RuleFor(x => x.FixedStartTimes)
        .NotNull().Must(t => t!.Count > 0)
        .WithMessage("At least one fixed start time is required.");
    }).Otherwise(() =>
    {
      RuleFor(x => x.WorkRanges).NotNull().Must(r => r.Count > 0).WithMessage("At least one work range is required.");
      RuleForEach(x => x.WorkRanges).SetValidator(new TimeRangeDtoValidator());
      RuleFor(x => x.Breaks).NotNull();
      RuleForEach(x => x.Breaks).SetValidator(new TimeRangeDtoValidator());
    });
  }
}

public sealed class UpdateShiftTemplateCommandValidator : AppValidator<UpdateShiftTemplateCommand>
{
  public UpdateShiftTemplateCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.Name);

    When(x => x.SlotGenerationMode == SlotGenerationMode.FixedStartTimes, () =>
    {
      RuleFor(x => x.FixedStartTimes)
        .NotNull().Must(t => t!.Count > 0)
        .WithMessage("At least one fixed start time is required.");
    }).Otherwise(() =>
    {
      RuleFor(x => x.WorkRanges).NotNull().Must(r => r.Count > 0).WithMessage("At least one work range is required.");
      RuleForEach(x => x.WorkRanges).SetValidator(new TimeRangeDtoValidator());
      RuleFor(x => x.Breaks).NotNull();
      RuleForEach(x => x.Breaks).SetValidator(new TimeRangeDtoValidator());
    });
  }
}

public sealed class DeleteShiftTemplateCommandValidator : AppValidator<DeleteShiftTemplateCommand>
{
  public DeleteShiftTemplateCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CreateServiceCategoryCommandValidator : AppValidator<CreateServiceCategoryCommand>
{
  public CreateServiceCategoryCommandValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.LongName);
    RuleFor(x => x.OrderIndex).GreaterThanOrEqualTo(0);
  }
}

public sealed class UpdateServiceCategoryCommandValidator : AppValidator<UpdateServiceCategoryCommand>
{
  public UpdateServiceCategoryCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.LongName);
    RuleFor(x => x.OrderIndex).GreaterThanOrEqualTo(0);
  }
}

public sealed class DeleteServiceCategoryCommandValidator : AppValidator<DeleteServiceCategoryCommand>
{
  public DeleteServiceCategoryCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetServiceCategoryByIdQueryValidator : AppValidator<GetServiceCategoryByIdQuery>
{
  public GetServiceCategoryByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CreateVatRateCommandValidator : AppValidator<CreateVatRateCommand>
{
  public CreateVatRateCommandValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.ShortName);
    RuleFor(x => x.Value).InclusiveBetween(0, 1);
  }
}

public sealed class UpdateVatRateCommandValidator : AppValidator<UpdateVatRateCommand>
{
  public UpdateVatRateCommandValidator()
  {
    RuleFor(x => x.Id).NotEmpty();
    RuleFor(x => x.Name).NotEmpty().MaximumLength(ValidationLimits.ShortName);
    RuleFor(x => x.Value).InclusiveBetween(0, 1);
  }
}

public sealed class DeleteVatRateCommandValidator : AppValidator<DeleteVatRateCommand>
{
  public DeleteVatRateCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetVatRateByIdQueryValidator : AppValidator<GetVatRateByIdQuery>
{
  public GetVatRateByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>
/// Na wspólnym rdzeniu (`PlaceAppointmentCommand`), nie na osłonie personelu — inaczej publiczny
/// booking, który wysyła rdzeń bezpośrednio, przestałby być walidowany.
/// </summary>
public sealed class PlaceAppointmentCommandValidator : AppValidator<PlaceAppointmentCommand>
{
  public PlaceAppointmentCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług w wizycie.");
    RuleFor(x => x.CustomerId).Must(NotEmptyIfPresent);
    RuleFor(x => x.CustomerPhone)
        .MaximumLength(ValidationLimits.Phone)
        .Must(IsValidPhoneNumber)
        .When(x => !string.IsNullOrWhiteSpace(x.CustomerPhone))
        .WithMessage("Podany numer telefonu jest nieprawidłowy.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
    RuleFor(x => x).Must(x => !x.CreateAsBooked || x.HoldLease is null)
        .WithMessage("Booked appointments must not include a hold lease.");
    RuleFor(x => x).Must(x => !x.CreateAsCompleted || (!x.CreateAsBooked && !x.CreateAsAwaitingOtp && x.HoldLease is null))
        .WithMessage("Completed appointments cannot be combined with Booked/AwaitingOtp/HoldLease.");
  }
}

public sealed class ApplyRescheduleCommandValidator : AppValidator<ApplyRescheduleCommand>
{
  public ApplyRescheduleCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług w wizycie.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class SwapAppointmentsCommandValidator : AppValidator<App.Application.Appointments.Commands.SwapAppointments.SwapAppointmentsCommand>
{
  public SwapAppointmentsCommandValidator()
  {
    RuleFor(x => x.FirstAppointmentId).NotEmpty();
    RuleFor(x => x.SecondAppointmentId).NotEmpty();
    RuleFor(x => x.SecondAppointmentId)
      .NotEqual(x => x.FirstAppointmentId)
      .WithMessage("Nie można zamienić wizyty z samą sobą.");
  }
}

public sealed class DeleteAppointmentCommandValidator : AppValidator<DeleteAppointmentCommand>
{
  public DeleteAppointmentCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateAppointmentStatusCommandValidator : AppValidator<UpdateAppointmentStatusCommand>
{
  public UpdateAppointmentStatusCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.StatusId).Must(id => AppointmentStatus.All.Any(s => s.Id == id));
  }
}

public sealed class UpdateAppointmentNotesCommandValidator : AppValidator<UpdateAppointmentNotesCommand>
{
  public UpdateAppointmentNotesCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.newNotes).MaximumLength(ValidationLimits.Notes);
  }
}

public sealed class SetFinalPriceCommandValidator : AppValidator<SetFinalPriceCommand>
{
  public SetFinalPriceCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    RuleFor(x => x.Currency).NotEmpty().Length(3);
  }
}

public sealed class GetAppointmentByIdQueryValidator : AppValidator<GetAppointmentByIdQuery>
{
  public GetAppointmentByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetCustomerAppointmentsQueryValidator : AppValidator<GetCustomerAppointmentsQuery>
{
  public GetCustomerAppointmentsQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetAvailableTimeSlotsQueryValidator : AppValidator<GetAvailableTimeSlotsQuery>
{
  public GetAvailableTimeSlotsQueryValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class GetAppointmentsByRangeQueryValidator : AppValidator<GetAppointmentsByRangeQuery>
{
  // Górny limit szerokości okna — bez tego złośliwy/przypadkowy zakres (np. 10 lat) materializuje
  // ogromny join appointments×employees×services×customers. 366 dni pokrywa każdy realny widok
  // kalendarza (rok), a odcina nieograniczone zapytanie.
  private const int MaxRangeDays = 366;

  public GetAppointmentsByRangeQueryValidator()
  {
    RuleFor(x => x.StartDate).Must(NotDefaultDate);
    RuleFor(x => x.EndDate).Must(NotDefaultDate).GreaterThanOrEqualTo(x => x.StartDate);
    RuleFor(x => x.EmployeeId).Must(NotEmptyIfPresent);
    RuleFor(x => x)
      .Must(x => x.EndDate.DayNumber - x.StartDate.DayNumber <= MaxRangeDays)
      .WithMessage($"Zakres dat nie może przekraczać {MaxRangeDays} dni.");
  }
}

/// <summary>
/// Dzwonek panelu poll'uje to zapytanie co kilka sekund, a handler materializuje wizyty razem
/// z grafikami pracowników i liczy dostępność w pamięci. Bez capa zakresu jeden request z
/// <c>From=0001-01-01, To=9999-12-31</c> ciągnie całą historię tenanta — i to w pętli pollingu.
/// </summary>
public sealed class GetOutsideScheduleAppointmentsQueryValidator
  : AppValidator<GetOutsideScheduleAppointmentsQuery>
{
  private const int MaxRangeDays = 366;

  public GetOutsideScheduleAppointmentsQueryValidator()
  {
    RuleFor(x => x.From).Must(NotDefaultDate);
    RuleFor(x => x.To).Must(NotDefaultDate).GreaterThanOrEqualTo(x => x.From);
    RuleFor(x => x)
      .Must(x => x.To.DayNumber - x.From.DayNumber <= MaxRangeDays)
      .WithMessage($"Zakres dat nie może przekraczać {MaxRangeDays} dni.");
  }
}

public sealed class GetPublicBookingSalonQueryValidator : AppValidator<GetPublicBookingSalonQuery>
{
  public GetPublicBookingSalonQueryValidator()
  {
    RuleFor(x => x.Slug).NotEmpty().MaximumLength(ValidationLimits.Slug).Must(IsSlug);
  }
}

public sealed class GetRegisterOwnerSlugAvailabilityQueryValidator : AppValidator<GetRegisterOwnerSlugAvailabilityQuery>
{
  public GetRegisterOwnerSlugAvailabilityQueryValidator()
  {
    RuleFor(x => x.Slug)
      .NotEmpty()
      .MaximumLength(ValidationLimits.Slug)
      .Must(IsSlug)
      .WithMessage("Slug can contain only letters, digits and hyphens.");
  }
}

public sealed class GetBookingEmployeesQueryValidator : AppValidator<GetBookingEmployeesQuery>
{
  public GetBookingEmployeesQueryValidator() =>
    RuleFor(x => x.ServiceIds)
      .Must(ids => ids == null || ids.All(id => id != Guid.Empty))
      .WithMessage("Lista usług zawiera nieprawidłowy identyfikator.");
}

public sealed class GetBookingServicesQueryValidator : AppValidator<GetBookingServicesQuery>
{
  public GetBookingServicesQueryValidator() => RuleFor(x => x.CategoryId).Must(NotEmptyIfPresent);
}

public sealed class GetBookingServiceByIdQueryValidator : AppValidator<GetBookingServiceByIdQuery>
{
  public GetBookingServiceByIdQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetBookingAvailableTimeSlotsQueryValidator : AppValidator<GetBookingAvailableTimeSlotsQuery>
{
  public GetBookingAvailableTimeSlotsQueryValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class GetBookingMonthAvailabilityQueryValidator : AppValidator<GetBookingMonthAvailabilityQuery>
{
  public GetBookingMonthAvailabilityQueryValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    // Cap długości listy usług (MaxServices) ucina mnożnik kosztu wyliczania dostępności miesiąca.
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
  }
}

public sealed class GetAppointmentMonthAvailabilityQueryValidator : AppValidator<GetAppointmentMonthAvailabilityQuery>
{
  public GetAppointmentMonthAvailabilityQueryValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
  }
}

public sealed class CreateBookingAppointmentCommandValidator : AppValidator<CreateBookingAppointmentCommand>
{
  public CreateBookingAppointmentCommandValidator()
  {
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class UpdatePublicAppointmentCommandValidator : AppValidator<UpdatePublicAppointmentCommand>
{
  public UpdatePublicAppointmentCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.Token).NotEmpty();
    RuleFor(x => x.EmployeeId).NotEmpty();
    RuleFor(x => x.ServiceIds).Must(IsValidServiceIdList).WithMessage("Nieprawidłowa lista usług.");
    RuleFor(x => x.Date).Must(NotDefaultDate);
  }
}

public sealed class RequestOtpCommandValidator : AppValidator<RequestOtpCommand>
{
  public RequestOtpCommandValidator()
  {
    RuleFor(x => x.Token).NotEmpty();
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.PhoneNumber)
        .MaximumLength(ValidationLimits.Phone)
        .Must(IsValidPhoneNumber)
        .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
    RuleFor(x => x.Email)
        .MaximumLength(ValidationLimits.Email)
        .EmailAddress()
        .When(x => !string.IsNullOrWhiteSpace(x.Email));
    RuleFor(x => x)
        .Must(x => !string.IsNullOrWhiteSpace(x.PhoneNumber) || !string.IsNullOrWhiteSpace(x.Email))
        .WithMessage("PhoneNumber or Email is required.");
    RuleFor(x => x.FirstName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Imię zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.FirstName));
    RuleFor(x => x.LastName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Nazwisko zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.LastName));
  }
}

public sealed class VerifyOtpCommandValidator : AppValidator<VerifyOtpCommand>
{
  public VerifyOtpCommandValidator()
  {
    RuleFor(x => x.Token).NotEmpty();
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.Otp).NotEmpty().Matches(@"^\d{6}$");
    RuleFor(x => x.FirstName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Imię zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.FirstName));
    RuleFor(x => x.LastName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Nazwisko zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.LastName));
    RuleFor(x => x.InstagramNick)
        .MaximumLength(30)
        .Matches(@"^@?[A-Za-z0-9._]+$").WithMessage("Nick na Instagramie zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.InstagramNick));
  }
}

public sealed class ConfirmBookingWithSessionCommandValidator : AppValidator<ConfirmBookingWithSessionCommand>
{
  public ConfirmBookingWithSessionCommandValidator()
  {
    RuleFor(x => x.Slug).NotEmpty();
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.Token).NotEmpty();
    RuleFor(x => x.SessionToken).NotEmpty();
    RuleFor(x => x.FirstName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Imię zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.FirstName));
    RuleFor(x => x.LastName)
        .MaximumLength(ValidationLimits.Name)
        .Matches(@"^[\p{L}\p{M} .'\-]+$").WithMessage("Nazwisko zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.LastName));
    RuleFor(x => x.InstagramNick)
        .MaximumLength(30)
        .Matches(@"^@?[A-Za-z0-9._]+$").WithMessage("Nick na Instagramie zawiera niedozwolone znaki.")
        .When(x => !string.IsNullOrWhiteSpace(x.InstagramNick));
  }
}
