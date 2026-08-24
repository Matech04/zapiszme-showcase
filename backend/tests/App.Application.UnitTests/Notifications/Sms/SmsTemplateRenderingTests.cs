using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.TenantAggregate;
using App.Application.Notifications.SmsTemplates.Queries.PreviewSmsTemplate;
using App.Domain.Exceptions;
using App.Infrastructure.Notifications.Sms;

namespace App.Application.UnitTests.Notifications.Sms;

/// <summary>
/// SMS-TPL-RENDER-* — interpolacja placeholderów, sanityzacja kosztowa (ASCII/limit) zastosowana do
/// custom treści oraz walidacja długości 70/140 i dozwolonych placeholderów.
/// </summary>
public sealed class SmsTemplateRenderingTests
{
  private static NotificationPayload Payload(
    string salon = "Studio Ann",
    string customer = "Anna",
    string service = "Manicure") => new(
      SalonName: salon,
      CustomerName: customer,
      StaffName: "Kasia",
      ServiceName: service,
      Date: new DateOnly(2026, 6, 25),
      StartTime: new TimeOnly(14, 30),
      OldDate: new DateOnly(2026, 6, 20),
      OldStartTime: new TimeOnly(10, 0),
      ActionUrl: "https://zps.me/x",
      AmountText: "50 PLN");

  [Fact]
  public void Render_InterpolatesPlaceholdersFromPayload()
  {
    var text = SmsTemplateRenderer.Render(
      "{salon}: wizyta {data} {godzina} ({usluga}) dla {klient}.", Payload());

    Assert.Equal("Studio Ann: wizyta 25.06 14:30 (Manicure) dla Anna.", text);
  }

  [Fact]
  public void Render_RescheduleOldDatePlaceholders()
  {
    var text = SmsTemplateRenderer.Render("{staraData} {staraGodzina} -> {data} {godzina}", Payload());
    Assert.Equal("20.06 10:00 -> 25.06 14:30", text);
  }

  [Fact]
  public void Render_UnknownPlaceholder_IsRemoved()
  {
    var text = SmsTemplateRenderer.Render("a{nieznany}b", Payload());
    Assert.Equal("ab", text);
  }

  [Fact]
  public void Sanitize_TransliteratesPolishToAscii_ForDefaultTemplates()
  {
    // Sanitize (używana przez DOMYŚLNE/hardcoded szablony) transliteruje do ASCII — defaulty są
    // zaprojektowane na GSM-7/140, więc payload z polskimi znakami zostaje przepisany na ASCII.
    var rendered = SmsTemplateRenderer.Render("Zażółć {salon} — wizyta", Payload(salon: "Łódź"));
    var sanitized = SmsTextBuilder.Sanitize(rendered);

    Assert.Equal("Zazolc Lodz - wizyta", sanitized);
    foreach (var ch in sanitized)
    {
      Assert.True(ch <= 127);
    }
  }

  [Fact]
  public void SanitizeUnicode_PreservesPolishAndEmoji()
  {
    // Custom treść ZACHOWUJE polskie znaki i emoji; zwija tylko nadmiarowe białe znaki.
    var text = SmsTextBuilder.SanitizeUnicode("Zażółć 🎉  gęślą");
    Assert.Equal("Zażółć 🎉 gęślą", text);
  }

  [Fact]
  public void SanitizeUnicode_ClipsUnicodeTo70_WithoutSplittingEmoji()
  {
    // 69 polskich znaków + emoji (2 jednostki UTF-16) = 71 > 70 → clip do 70, ale emoji na granicy
    // jest usuwane w CAŁOŚCI (nie zostaje osierocony high surrogate → uszkodzony znak).
    var body = new string('ą', 69) + "🎉";
    var text = SmsTextBuilder.SanitizeUnicode(body);

    Assert.True(text.Length <= 70);
    Assert.DoesNotContain('\uD83C', text);
  }

  [Fact]
  public void SanitizeUnicode_AsciiOnly_KeepsLimit140()
  {
    var body = new string('a', 200);
    var text = SmsTextBuilder.SanitizeUnicode(body);
    Assert.Equal(140, text.Length); // sama łacina → GSM-7 → próg 140
  }

  [Fact]
  public void Sanitize_ClipsToMaxLength()
  {
    var rendered = SmsTemplateRenderer.Render(new string('A', 300), Payload());
    var sanitized = SmsTextBuilder.Sanitize(rendered);
    Assert.True(sanitized.Length <= SmsTextBuilder.MaxLength);
  }

  // ── długość 70 (UCS-2) vs 140 (GSM-7) ──────────────────────────────────────────────────

  [Fact]
  public void Length_AsciiOnly_Limit140()
  {
    Assert.False(SmsLengthCalculator.RequiresUnicode("Twoja wizyta jutro o 14:30."));
    Assert.Equal(140, SmsLengthCalculator.LimitFor("Twoja wizyta jutro o 14:30."));
  }

  [Fact]
  public void Length_PolishChars_Limit70()
  {
    Assert.True(SmsLengthCalculator.RequiresUnicode("Zażółć gęślą jaźń"));
    Assert.Equal(70, SmsLengthCalculator.LimitFor("Zażółć gęślą jaźń"));
  }

  // ── walidacja ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public void Validate_NonCustomizableType_Throws()
  {
    var ex = Assert.Throws<SmsTemplateException>(() =>
      SmsTemplateValidator.Validate(NotificationType.NewBookingToSalon, "tresc"));
    Assert.Equal(ErrorCodes.SmsTemplateTypeNotCustomizable, ex.ErrorCode);
  }

  [Fact]
  public void Validate_Otp_NotCustomizable()
  {
    Assert.False(SmsTemplatePlaceholders.IsCustomizable(NotificationType.CustomerVerificationOtp));
  }

  /// <summary>
  /// Makra smsapi interpretuje dostawca. Moderator w polskim UI nie rozpozna [%…%] jako aktywnej
  /// składni, a zatwierdzony szablon poszedłby do wszystkich klientek salonu.
  /// </summary>
  [Theory]
  [InlineData("Witaj {klient}, [%idzdo:evil.pl%]")]
  [InlineData("Witaj {klient} %] reszta")]
  public void Validate_BodyWithSmsapiMacro_Throws(string body)
  {
    var ex = Assert.Throws<SmsTemplateException>(() =>
      SmsTemplateValidator.Validate(NotificationType.BookingConfirmationToCustomer, body));
    Assert.Equal(ErrorCodes.SmsTemplateInvalidPlaceholder, ex.ErrorCode);
  }

  [Fact]
  public void Validate_TooLongAsciiOverGsm_Throws()
  {
    var body = new string('a', 141);
    var ex = Assert.Throws<SmsTemplateException>(() =>
      SmsTemplateValidator.Validate(NotificationType.BookingConfirmationToCustomer, body));
    Assert.Equal(ErrorCodes.SmsTemplateTooLong, ex.ErrorCode);
  }

  [Fact]
  public void Validate_71PolishChars_Throws()
  {
    var body = new string('ą', 71); // UCS-2 → limit 70
    var ex = Assert.Throws<SmsTemplateException>(() =>
      SmsTemplateValidator.Validate(NotificationType.BookingConfirmationToCustomer, body));
    Assert.Equal(ErrorCodes.SmsTemplateTooLong, ex.ErrorCode);
  }

  [Fact]
  public void Validate_70PolishChars_Ok()
  {
    var body = new string('ą', 70);
    SmsTemplateValidator.Validate(NotificationType.BookingConfirmationToCustomer, body);
  }

  [Fact]
  public void Validate_DisallowedPlaceholderForType_Throws()
  {
    // {link} jest dozwolony tylko dla DepositLink — nie dla potwierdzenia rezerwacji.
    var ex = Assert.Throws<SmsTemplateException>(() =>
      SmsTemplateValidator.Validate(NotificationType.BookingConfirmationToCustomer, "kliknij {link}"));
    Assert.Equal(ErrorCodes.SmsTemplateInvalidPlaceholder, ex.ErrorCode);
  }

  [Fact]
  public void Validate_AllowedPlaceholders_Ok()
  {
    SmsTemplateValidator.Validate(
      NotificationType.RescheduleToCustomer,
      "{salon}: z {staraData} {staraGodzina} na {data} {godzina}");
  }

  // ── podgląd (PreviewSmsTemplateQuery) — to, co realnie zobaczy klient ─────────────────────

  [Fact]
  public async Task PreviewQuery_PreservesPolishAndEmoji()
  {
    var handler = new PreviewSmsTemplateQueryHandler(new SmsDefaultTemplateProvider());

    var result = await handler.Handle(
      new PreviewSmsTemplateQuery(
        NotificationType.BookingConfirmationToCustomer,
        "{salon}: wizyta {data} ({usluga}) 🎉 do zażółcenia!"),
      CancellationToken.None);

    // SamplePayload: salon „Twoj Salon", usługa „Manicure", data 25.06. Custom treść NIE jest
    // transliterowana — polskie znaki i emoji zachowane (świadomy wybór salonu, SMS leci UCS-2).
    Assert.Equal("Twoj Salon: wizyta 25.06 (Manicure) 🎉 do zażółcenia!", result.Preview);
  }
}
