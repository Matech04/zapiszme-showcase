using App.Application.Booking;
using App.Application.Common;
using App.Application.Common.Behaviors;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace App.Application;

public static class DependencyInjection
{
  /// <summary>
  /// Rejestruje application-layer services. <paramref name="configuration"/> (opcjonalne)
  /// pozwala odczytać <c>MediatR:LicenseKey</c> — wymagane w produkcji od MediatR 14
  /// (Lucky Penny Software licensing). W Development/Testing klucz nie jest wymagany.
  /// Klucz NIE jest commitowany do repo — pochodzi z env-var <c>MediatR__LicenseKey</c>
  /// (GH Secret → docker-compose env).
  /// </summary>
  public static IServiceCollection AddApplicationServices(
    this IServiceCollection services,
    IConfiguration? configuration = null)
  {
    var assembly = Assembly.GetExecutingAssembly();

    services.AddMediatR(cfg =>
    {
      cfg.RegisterServicesFromAssembly(assembly);
      var licenseKey = configuration?["MediatR:LicenseKey"];
      if (!string.IsNullOrWhiteSpace(licenseKey))
      {
        cfg.LicenseKey = licenseKey;
      }
    });
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BookingPauseBehavior<,>));

    services.AddValidatorsFromAssembly(assembly);

    services.AddScoped<IDeletionService, DeletionService>();
    // SCOPED, nie singleton: zależy od ICurrentUserAccessor i DbContext (oba scoped), a do tego
    // pamięta politykę tenanta na czas żądania. Singleton = captive dependency + wyciek tożsamości.
    services.AddScoped<IStaffAccessPolicy, StaffAccessPolicy>();
    services.AddScoped<IAppointmentInspirationCleanup, AppointmentInspirationCleanup>();
    services.AddSingleton<IVatRateCatalog, VatRateCatalog>();
    services.AddScoped<ITenantVatRateSeeder, TenantVatRateSeeder>();

    // Default real-clock provider. W env "E2E" Program.cs nadpisuje na FakeTimeProvider,
    // co pozwala testom E2E przesuwać czas (Hold lease expiry, OTP expiry, BG cycles)
    // bez czekania realnych sekund/minut.
    services.TryAddSingleton(TimeProvider.System);

    var bookingSection = configuration?.GetSection(BookingHoldOptions.Section);
    if (bookingSection is not null)
    {
      services.Configure<BookingHoldOptions>(bookingSection);
    }
    else
    {
      services.Configure<BookingHoldOptions>(_ => { });
    }

    return services;
  }
}
