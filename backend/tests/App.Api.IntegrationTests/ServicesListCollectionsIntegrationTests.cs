using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Services.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Lista usług musi przypiąć KAŻDĄ kolekcję do właściwej usługi.
///
/// Powód powstania: handler przeszedł z jednej projekcji z trzema kolekcjami (Images, Addons,
/// EmployeeIds) na cztery płaskie zapytania składane w pamięci — poprzednia wersja generowała
/// iloczyn kartezjański (zmierzone na produkcji 82 wiersze zamiast 21). Przy takiej przebudowie
/// realnym ryzykiem nie jest wydajność, tylko ZŁE ZŁĄCZENIE: kolekcja doklejona do sąsiedniej
/// usługi albo pusta lista tam, gdzie coś być powinno. Dlatego seed celowo daje każdej usłudze
/// INNY zestaw i inną liczność.
///
/// CZEGO TEN TEST NIE PILNUJE: samego braku iloczynu kartezjańskiego. Próba oparcia asercji na
/// ostrzeżeniu EF `MultipleCollectionIncludeWarning` okazała się niewykonalna — ostrzeżenie pada
/// RAZ NA KOMPILACJĘ kształtu zapytania, a ta jest cache'owana poza zasięgiem testu. Sprawdzone:
/// uruchomiony sam, test łapał starą implementację; uruchomiony obok drugiego testu, który wcześniej
/// odpytał ten sam endpoint — przechodził na zielono mimo kartezjanu. Strażnik zależny od kolejności
/// jest gorszy niż jego brak, więc go tu nie ma. Regresję wydajności trzeba wyłapać w logu
/// produkcyjnym (ostrzeżenia po deployu) albo ręcznym `ToQueryString`.
/// </summary>
public sealed class ServicesListCollectionsIntegrationTests
{
  [Fact]
  public async Task Kazda_usluga_dostaje_swoje_zdjecia_dodatki_i_pracownikow()
  {
    using var factory = new BookingApiApplicationFactory();
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    var (zDodatkami, dodatekA, dodatekB, bezNiczego, drugiPracownik) =
      SeedUslugi(factory.Services, seed);

    var client = factory.CreateOwnerClient();
    var response = await client.GetAsync("/api/Services", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var uslugi = await response.Content.ReadFromJsonAsync<List<ServiceDto>>(ct);
    Assert.NotNull(uslugi);

    var glowna = Assert.Single(uslugi!, s => s.Id == zDodatkami);
    var pusta = Assert.Single(uslugi!, s => s.Id == bezNiczego);

    // Zdjęcia: tylko przy „głównej", posortowane po OrderIndex (pierwsze = cover).
    Assert.NotNull(glowna.Images);
    Assert.Equal(2, glowna.Images!.Count);
    Assert.Equal("https://cdn.local/a.jpg", glowna.Images[0].Url);
    Assert.Equal("https://cdn.local/b.jpg", glowna.Images[1].Url);

    // Dodatki: dwa, dokładnie te przypisane.
    Assert.NotNull(glowna.AddonServiceIds);
    Assert.Equal(
      new[] { dodatekA, dodatekB }.OrderBy(x => x).ToList(),
      glowna.AddonServiceIds!.OrderBy(x => x).ToList());

    // Pracownicy: obaj przypisani do „głównej".
    Assert.Equal(
      new[] { seed.EmployeeId, drugiPracownik }.OrderBy(x => x).ToList(),
      glowna.EmployeeIds.OrderBy(x => x).ToList());

    // Usługa bez niczego MUSI dostać puste listy, nie null i nie cudze wpisy.
    Assert.Empty(pusta.Images ?? []);
    Assert.Empty(pusta.AddonServiceIds ?? []);
    Assert.Empty(pusta.EmployeeIds);

    // Dodatki same w sobie są usługami — nie mogą przejąć zdjęć ani pracowników „głównej".
    var dodatek = Assert.Single(uslugi!, s => s.Id == dodatekA);
    Assert.Empty(dodatek.Images ?? []);
    Assert.Empty(dodatek.EmployeeIds);
  }

  private static (Guid zDodatkami, Guid dodatekA, Guid dodatekB, Guid bezNiczego, Guid drugiPracownik)
    SeedUslugi(IServiceProvider rootServices, RestApiIntegrationSeedResult seed)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // OSOBNA instancja Money na każdą usługę. `Money` to owned entity, a EF deduplikuje je PO
    // REFERENCJI: jedna współdzielona instancja zostaje przypisana do pierwszego właściciela,
    // a pozostali dostają NULL-e i zapis pada na `null value in column "price_amount"`.
    static Money Cena() => new(100m, "PLN");

    var dodatekA = new Service(seed.TenantId, seed.ServiceCategoryId, seed.VatRateId, "Dodatek A", Cena(), 15, isAddon: true);
    var dodatekB = new Service(seed.TenantId, seed.ServiceCategoryId, seed.VatRateId, "Dodatek B", Cena(), 20, isAddon: true);
    var bezNiczego = new Service(seed.TenantId, seed.ServiceCategoryId, seed.VatRateId, "Bez niczego", Cena(), 30);
    var zDodatkami = new Service(seed.TenantId, seed.ServiceCategoryId, seed.VatRateId, "Z dodatkami", Cena(), 45);

    db.Services.AddRange(dodatekA, dodatekB, bezNiczego, zDodatkami);
    db.SaveChanges();

    // `SetImages` nadaje OrderIndex wg kolejności wejścia — odpowiedź ma wrócić w TEJ kolejności,
    // niezależnie od tego, jak wiersze ułoży baza (pierwsze zdjęcie = cover galerii).
    zDodatkami.SetImages(
    [
      new ServiceImageData("https://cdn.local/a.jpg", "https://cdn.local/a-thumb.jpg", "key-a"),
      new ServiceImageData("https://cdn.local/b.jpg", "https://cdn.local/b-thumb.jpg", "key-b"),
    ]);
    zDodatkami.SetAddons([dodatekA.Id, dodatekB.Id]);

    var drugiPracownik = new Employee(seed.TenantId, userId: null, "Druga", "Osoba", "druga@services-list.local");
    db.Employees.Add(drugiPracownik);
    db.SaveChanges();

    var pierwszy = db.Employees.IgnoreQueryFilters().Include(e => e.Services).Single(e => e.Id == seed.EmployeeId);
    if (pierwszy.Services.All(s => s.ServiceId != zDodatkami.Id))
    {
      pierwszy.AssignService(seed.TenantId, zDodatkami.Id, customDuration: null, customPrice: null);
    }

    drugiPracownik.AssignService(seed.TenantId, zDodatkami.Id, customDuration: null, customPrice: null);
    db.SaveChanges();

    return (zDodatkami.Id, dodatekA.Id, dodatekB.Id, bezNiczego.Id, drugiPracownik.Id);
  }
}
