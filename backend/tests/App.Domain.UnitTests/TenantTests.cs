using App.Domain.Aggregates.TenantAggregate;

namespace App.Domain.UnitTests;

public class TenantTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var name = "Salon Piękności";
    var slug = "salon-pieknosci";

    // Act
    var tenant = new Tenant(name, slug);

    // Assert
    Assert.NotEqual(Guid.Empty, tenant.Id);
    Assert.Equal(name, tenant.Name);
    Assert.Equal(slug, tenant.Slug);
    Assert.Equal(15, tenant.AppointmentSlotStepMinutes);
  }

  [Fact]
  public void Update_ShouldUpdateValuesCorrectly()
  {
    // Arrange
    var tenant = new Tenant("Original Name", "original-slug");
    var newName = "Updated Name";
    var newSlug = "updated-slug";

    // Act
    tenant.Update(newName, newSlug);

    // Assert
    Assert.Equal(newName, tenant.Name);
    Assert.Equal(newSlug, tenant.Slug);
  }

  [Fact]
  public void Update_WithSpacesInSlug_ShouldReplaceSpaces()
  {
    // Arrange
    var tenant = new Tenant("Name", "slug");
    var name = "New Name";
    var slugWithSpaces = "new slug with spaces";
    var expectedSlug = "new-slug-with-spaces";

    // Act
    tenant.Update(name, slugWithSpaces);

    // Assert
    Assert.Equal(expectedSlug, tenant.Slug);
  }

  [Theory]
  [InlineData("", "slug")]
  [InlineData("Name", "")]
  [InlineData(null, "slug")]
  [InlineData("Name", null)]
  public void Constructor_WithInvalidInput_ShouldThrowException(string name, string slug)
  {
    // Act & Assert
    Assert.ThrowsAny<Exception>(() => new Tenant(name!, slug!));
  }

  [Fact]
  public void Update_WithAppointmentSlotStep_ShouldPersistValue()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.Update("Name", "slug", appointmentSlotStepMinutes: 30);

    Assert.Equal(30, tenant.AppointmentSlotStepMinutes);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(241)]
  public void Update_WithInvalidAppointmentSlotStep_ShouldThrow(int minutes)
  {
    var tenant = new Tenant("Name", "slug");

    Assert.ThrowsAny<Exception>(() => tenant.Update("Name", "slug", appointmentSlotStepMinutes: minutes));
  }

  [Fact]
  public void Update_BookingCalendarColorHex_SetsClearsAndKeeps()
  {
    var tenant = new Tenant("Name", "slug");
    Assert.Null(tenant.BookingCalendarColorHex);

    // Ustawienie — normalizujemy do wielkich liter.
    tenant.Update("Name", "slug", bookingCalendarColorHex: "#ff5733");
    Assert.Equal("#FF5733", tenant.BookingCalendarColorHex);

    // null = brak zmiany (konwencja Update — np. zwykła edycja nazwy nie kasuje koloru).
    tenant.Update("Name", "slug");
    Assert.Equal("#FF5733", tenant.BookingCalendarColorHex);

    // "" = wyczyść do motywu domyślnego.
    tenant.Update("Name", "slug", bookingCalendarColorHex: "");
    Assert.Null(tenant.BookingCalendarColorHex);
  }

  [Fact]
  public void Update_BookingCalendarThemeColors_SetAndClearIndependently()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.Update(
      "Name", "slug",
      bookingCalendarBackgroundHex: "#fdf2f8",
      bookingCalendarSurfaceHex: "#ffffff",
      bookingCalendarPriceHex: "#0d9488");
    Assert.Equal("#FDF2F8", tenant.BookingCalendarBackgroundHex);
    Assert.Equal("#FFFFFF", tenant.BookingCalendarSurfaceHex);
    Assert.Equal("#0D9488", tenant.BookingCalendarPriceHex);

    // Wyczyszczenie tylko ceny nie rusza pozostałych.
    tenant.Update("Name", "slug", bookingCalendarPriceHex: "");
    Assert.Null(tenant.BookingCalendarPriceHex);
    Assert.Equal("#FDF2F8", tenant.BookingCalendarBackgroundHex);
    Assert.Equal("#FFFFFF", tenant.BookingCalendarSurfaceHex);
  }

  [Fact]
  public void Constructor_ShouldDefaultNotificationSettingsToDefaults()
  {
    var tenant = new Tenant("Name", "slug");

    foreach (NotificationType type in Enum.GetValues<NotificationType>())
    {
      // Typy staff-triggered nie są sterowane ustawieniami: CustomerVerificationOtp (zawsze
      // wymagany w flow rezerwacji) oraz DepositLinkToCustomer (jawna akcja personelu).
      if (type is NotificationType.CustomerVerificationOtp or NotificationType.DepositLinkToCustomer)
      {
        continue;
      }

      // Domyślnie wyłączone dla nowego salonu: potwierdzenie rezerwacji do klienta i przypomnienie 2h.
      var expected = type is not NotificationType.BookingConfirmationToCustomer
                            and not NotificationType.AppointmentReminder2hToCustomer;
      Assert.Equal(expected, tenant.NotificationSettings.IsEnabled(type));
    }
  }

  [Fact]
  public void Constructor_ShouldDefaultToSmsFirst()
  {
    var tenant = new Tenant("Name", "slug");

    // SMS-first: klient potwierdza rezerwację (i dostaje powiadomienia) kanałem SMS.
    Assert.Equal(CustomerVerificationChannel.Phone, tenant.CustomerVerificationChannel);
  }

  [Fact]
  public void Update_WithNotificationSettings_ShouldReplaceThem()
  {
    var tenant = new Tenant("Name", "slug");
    var custom = new NotificationSettings(false, false, false, false, false, false, false, false, false, false, false, false);

    tenant.Update("Name", "slug", notificationSettings: custom);

    Assert.Same(custom, tenant.NotificationSettings);
    Assert.False(tenant.NotificationSettings.IsEnabled(NotificationType.NewBookingToSalon));
  }

  [Fact]
  public void Update_WithNullNotificationSettings_ShouldLeaveThemUnchanged()
  {
    var tenant = new Tenant("Name", "slug");
    var original = tenant.NotificationSettings;

    tenant.Update("Name", "slug", notificationSettings: null);

    Assert.Same(original, tenant.NotificationSettings);
  }

  [Fact]
  public void Constructor_ShouldDefaultRequireCustomerNameToFalse()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.False(tenant.RequireCustomerName);
  }

  [Fact]
  public void Update_WithRequireCustomerName_ShouldPersistValue()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.Update("Name", "slug", requireCustomerName: true);

    Assert.True(tenant.RequireCustomerName);
  }

  [Fact]
  public void Update_WithNullRequireCustomerName_ShouldLeaveItUnchanged()
  {
    var tenant = new Tenant("Name", "slug");
    tenant.Update("Name", "slug", requireCustomerName: true);

    tenant.Update("Name", "slug", requireCustomerName: null);

    Assert.True(tenant.RequireCustomerName);
  }

  [Fact]
  public void Constructor_ShouldDefaultCollectInstagramHandleToFalse()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.False(tenant.CollectInstagramHandle);
  }

  [Fact]
  public void Update_WithCollectInstagramHandle_ShouldPersistValue()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.Update("Name", "slug", collectInstagramHandle: true);

    Assert.True(tenant.CollectInstagramHandle);
  }

  [Fact]
  public void Update_WithNullCollectInstagramHandle_ShouldLeaveItUnchanged()
  {
    var tenant = new Tenant("Name", "slug");
    tenant.Update("Name", "slug", collectInstagramHandle: true);

    tenant.Update("Name", "slug", collectInstagramHandle: null);

    Assert.True(tenant.CollectInstagramHandle);
  }

  [Fact]
  public void Constructor_ShouldDefaultCollectInspirationImagesToFalse()
  {
    var tenant = new Tenant("Name", "slug");

    // Funkcja jest opt-in — salon świadomie włącza ją w ustawieniach.
    Assert.False(tenant.CollectInspirationImages);
  }

  [Fact]
  public void Update_WithCollectInspirationImages_ShouldPersistValue()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.Update("Name", "slug", collectInspirationImages: true);

    Assert.True(tenant.CollectInspirationImages);
  }

  [Fact]
  public void Update_WithNullCollectInspirationImages_ShouldLeaveItUnchanged()
  {
    var tenant = new Tenant("Name", "slug");
    tenant.Update("Name", "slug", collectInspirationImages: true);

    tenant.Update("Name", "slug", collectInspirationImages: null);

    Assert.True(tenant.CollectInspirationImages);
  }

  [Fact]
  public void Constructor_ShouldDefaultCustomDomainToNull()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.Null(tenant.CustomDomain);
  }

  [Theory]
  [InlineData("salon-przyklad.pl", "salon-przyklad.pl")]
  [InlineData("  Salon-Przyklad.PL  ", "salon-przyklad.pl")]
  [InlineData("EXAMPLE.COM", "example.com")]
  public void SetCustomDomain_ShouldNormalizeTrimAndLowercase(string input, string expected)
  {
    var tenant = new Tenant("Name", "slug");

    tenant.SetCustomDomain(input);

    Assert.Equal(expected, tenant.CustomDomain);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void SetCustomDomain_WithEmptyOrWhitespace_ShouldClearToNull(string? input)
  {
    var tenant = new Tenant("Name", "slug");
    tenant.SetCustomDomain("salon-przyklad.pl");

    tenant.SetCustomDomain(input);

    Assert.Null(tenant.CustomDomain);
  }

  [Fact]
  public void Constructor_ShouldDefaultTermsOfServiceToNull()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.Null(tenant.TermsOfService);
  }

  [Fact]
  public void Update_TermsOfService_SetsTrimsClearsAndKeeps()
  {
    var tenant = new Tenant("Name", "slug");

    // Ustawienie — przycinamy białe znaki na brzegach.
    tenant.Update("Name", "slug", termsOfService: "  Regulamin salonu  ");
    Assert.Equal("Regulamin salonu", tenant.TermsOfService);

    // null = brak zmiany (konwencja Update — zwykła edycja nazwy nie kasuje regulaminu).
    tenant.Update("Name", "slug");
    Assert.Equal("Regulamin salonu", tenant.TermsOfService);

    // "" (lub same białe znaki) = wyczyść regulamin do null.
    tenant.Update("Name", "slug", termsOfService: "   ");
    Assert.Null(tenant.TermsOfService);
  }

  [Fact]
  public void Constructor_ShouldDefaultBookingPauseOff()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.False(tenant.BookingPaused);
    Assert.Null(tenant.BookingPauseMessage);
  }

  [Fact]
  public void SetBookingPause_Enable_WithMessage_ShouldTrimAndStore()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.SetBookingPause(true, "  Zmiany w grafiku  ");

    Assert.True(tenant.BookingPaused);
    Assert.Equal("Zmiany w grafiku", tenant.BookingPauseMessage);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void SetBookingPause_Enable_WithBlankMessage_ShouldStoreNull(string? message)
  {
    var tenant = new Tenant("Name", "slug");

    tenant.SetBookingPause(true, message);

    Assert.True(tenant.BookingPaused);
    Assert.Null(tenant.BookingPauseMessage);
  }

  [Fact]
  public void SetBookingPause_Disable_ShouldClearMessage()
  {
    var tenant = new Tenant("Name", "slug");
    tenant.SetBookingPause(true, "Rezerwacje wstrzymane");

    tenant.SetBookingPause(false);

    Assert.False(tenant.BookingPaused);
    Assert.Null(tenant.BookingPauseMessage);
  }

  [Fact]
  public void SetBookingPause_Enable_WithOverlongMessage_ShouldClampToMaxLength()
  {
    var tenant = new Tenant("Name", "slug");
    var longMessage = new string('x', Tenant.BookingPauseMessageMaxLength + 50);

    tenant.SetBookingPause(true, longMessage);

    Assert.Equal(Tenant.BookingPauseMessageMaxLength, tenant.BookingPauseMessage!.Length);
  }

  [Fact]
  public void New_tenant_has_no_industry_and_no_onboarding_completed()
  {
    var tenant = new Tenant("Name", "slug");

    Assert.Null(tenant.Industry);
    Assert.Null(tenant.OnboardingCompletedAt);
  }

  [Fact]
  public void SetIndustry_trims_and_lowercases()
  {
    var tenant = new Tenant("Name", "slug");

    tenant.SetIndustry("  Barber ");

    Assert.Equal("barber", tenant.Industry);
  }

  [Fact]
  public void SetIndustry_blank_clears_to_null()
  {
    var tenant = new Tenant("Name", "slug");
    tenant.SetIndustry("nails");

    tenant.SetIndustry("   ");

    Assert.Null(tenant.Industry);
  }

  [Fact]
  public void MarkOnboardingCompleted_sets_utc_timestamp()
  {
    var tenant = new Tenant("Name", "slug");
    var now = DateTime.UtcNow;

    tenant.MarkOnboardingCompleted(now);

    Assert.NotNull(tenant.OnboardingCompletedAt);
    Assert.Equal(DateTimeKind.Utc, tenant.OnboardingCompletedAt!.Value.Kind);
  }

  [Fact]
  public void MarkOnboardingCompleted_is_idempotent_first_stamp_wins()
  {
    var tenant = new Tenant("Name", "slug");
    var first = DateTime.UtcNow.AddHours(-2);
    tenant.MarkOnboardingCompleted(first);

    tenant.MarkOnboardingCompleted(DateTime.UtcNow);

    Assert.Equal(first.ToUniversalTime(), tenant.OnboardingCompletedAt);
  }
}