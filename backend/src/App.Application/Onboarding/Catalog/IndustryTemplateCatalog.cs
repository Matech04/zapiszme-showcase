namespace App.Application.Onboarding.Catalog;

/// <summary>Usługa z szablonu branżowego: nazwa, cena (PLN) i czas trwania w minutach.</summary>
public sealed record TemplateService(string Name, decimal Price, int DurationMinutes);

/// <summary>
/// Szablon branżowy: klucz (zapisywany na <c>Tenant.Industry</c>), polska etykieta, domyślna nazwa
/// kategorii usług i lista propozycji usług. Branża „other" ma pustą listę i brak kategorii.
/// </summary>
public sealed record IndustryTemplate(
  string Key,
  string Label,
  string? CategoryName,
  IReadOnlyList<TemplateService> Services);

/// <summary>
/// Statyczny katalog szablonów branżowych kreatora onboardingu. To TREŚĆ PRODUKTOWA (nie dane
/// klienta), więc żyje w kodzie, nie w bazie. Wzorzec „nails" pochodzi z <c>DemoDataSeeder</c>.
/// Klik w branżę nic nie zapisuje — usługi są zapisywane dopiero przez <c>ApplyIndustryTemplateCommand</c>
/// z (możliwie zmodyfikowanego) zestawu przesłanego z frontu.
/// </summary>
public static class IndustryTemplateCatalog
{
  public static IReadOnlyList<IndustryTemplate> All { get; } =
  [
    new("nails", "Stylizacja paznokci", "Paznokcie i stylizacja",
    [
      new("Manicure hybrydowy", 120m, 60),
      new("Uzupełnienie żelowe", 160m, 90),
      new("Pedicure kosmetyczny", 150m, 75),
      new("Stylizacja brwi (henna pudrowa)", 80m, 45),
      new("Laminacja rzęs", 140m, 75),
    ]),

    new("lashes-brows", "Rzęsy i brwi", "Rzęsy i brwi",
    [
      new("Przedłużanie rzęs 1:1", 200m, 120),
      new("Rzęsy objętościowe 2-4D", 250m, 150),
      new("Laminacja rzęs", 140m, 75),
      new("Henna pudrowa brwi", 80m, 45),
      new("Regulacja i geometria brwi", 60m, 30),
      new("Laminacja brwi", 120m, 60),
    ]),

    new("hair", "Fryzjer", "Fryzjerstwo",
    [
      new("Strzyżenie damskie", 90m, 60),
      new("Strzyżenie męskie", 60m, 30),
      new("Koloryzacja", 250m, 150),
      new("Baleyage / Sombre", 350m, 180),
      new("Modelowanie / upięcie", 80m, 45),
      new("Zabieg pielęgnacyjny / botoks", 150m, 60),
    ]),

    new("barber", "Barber", "Barbering",
    [
      new("Strzyżenie włosów", 70m, 45),
      new("Strzyżenie włosów + broda", 110m, 60),
      new("Trymowanie brody", 50m, 30),
      new("Golenie brzytwą", 60m, 40),
      new("Strzyżenie maszynką", 40m, 20),
    ]),

    new("cosmetology", "Kosmetologia", "Zabiegi na twarz",
    [
      new("Oczyszczanie wodorowe", 200m, 75),
      new("Peeling kawitacyjny", 150m, 60),
      new("Mezoterapia igłowa", 350m, 60),
      new("Zabieg nawilżający", 180m, 60),
      new("Mikrodermabrazja", 160m, 60),
    ]),

    new("massage", "Masaż", "Masaże",
    [
      new("Masaż klasyczny (60 min)", 150m, 60),
      new("Masaż relaksacyjny (90 min)", 220m, 90),
      new("Masaż gorącymi kamieniami", 200m, 75),
      new("Masaż sportowy", 170m, 60),
      new("Drenaż limfatyczny", 180m, 60),
    ]),

    new("pmu", "Makijaż permanentny", "Makijaż permanentny",
    [
      new("Makijaż permanentny brwi (metoda pudrowa)", 600m, 180),
      new("Kreska permanentna na powiece", 500m, 120),
      new("Usta permanentne", 700m, 180),
      new("Dopigmentowanie (korekta)", 250m, 90),
    ]),

    new("depilation", "Depilacja", "Depilacja",
    [
      new("Depilacja woskiem – nogi całe", 120m, 45),
      new("Depilacja woskiem – pachy", 40m, 20),
      new("Depilacja woskiem – bikini", 80m, 30),
      new("Depilacja laserowa – wąsik", 60m, 20),
      new("Depilacja laserowa – nogi całe", 250m, 60),
    ]),

    new("other", "Inne", null, []),
  ];

  /// <summary>Zwraca szablon dla klucza branży (case-insensitive) lub <c>null</c>, gdy nieznany.</summary>
  public static IndustryTemplate? Find(string? key)
  {
    if (string.IsNullOrWhiteSpace(key))
    {
      return null;
    }

    var normalized = key.Trim().ToLowerInvariant();
    return All.FirstOrDefault(t => t.Key == normalized);
  }
}
