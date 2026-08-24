using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Commands.SetEmployeeSchedule;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Onboarding.Commands.SetOnboardingSchedule;

/// <summary>
/// Jeden dzień roboczy presetu. Godziny są <b>per dzień</b> — poniedziałek 9–17 i wtorek 10–18 to
/// normalny grafik, a wcześniej komenda miała jeden wspólny <c>StartTime</c>/<c>EndTime</c> dla
/// wszystkich dni i po cichu zapisywała złe godziny.
/// </summary>
/// <param name="StartTime">Początek pracy. <c>null</c> w trybie stałych godzin — patrz niżej.</param>
/// <param name="EndTime">
/// Koniec pracy. <c>null</c> w trybie stałych godzin: tam dzień nie ma okna pracy, tylko godziny
/// startu (wspólne dla wszystkich dni). Nullable zamiast atrapy, bo wcześniej front wymuszał
/// poprawny zakres, którego handler i tak nie używał (<c>Array.Empty&lt;TimeRangeDto&gt;()</c>).
/// </param>
public sealed record OnboardingScheduleDayInput(
  DayOfWeek Day,
  TimeOnly? StartTime,
  TimeOnly? EndTime);

/// <summary>
/// Krok kreatora „Kiedy pracujesz?" — cienki wrapper na istniejący
/// <see cref="SetEmployeeScheduleCommand"/> (nie wymyśla logiki grafiku na nowo). Rozwija dni
/// robocze z godzinami na grafik per-dzień i deleguje dla pracownika-właściciela.
/// Dla trybu siatki (<see cref="SlotGenerationMode.Grid"/>) włącza dodatkowo
/// <see cref="GapFillingMode.PreferAdjacent"/> na salonie (bez pytania — zgodnie z planem).
/// </summary>
/// <param name="UseAdHoc">
/// „Papierowy kalendarz": właściciel świadomie nie prowadzi grafiku powtarzalnego i będzie wpisywał
/// dostępność dzień po dniu (dni specjalne). Wtedy NIE tworzymy żadnego grafiku — zapisujemy samą
/// deklarację na pracowniku, a pozostałe pola komendy są ignorowane (i niewalidowane).
/// </param>
public sealed record SetOnboardingScheduleCommand(
  SlotGenerationMode SlotMode,
  IReadOnlyList<OnboardingScheduleDayInput> Days,
  IReadOnlyList<TimeOnly>? FixedStartTimes,
  bool UseAdHoc = false) : IRequest;

public sealed class SetOnboardingScheduleCommandValidator : AbstractValidator<SetOnboardingScheduleCommand>
{
  public SetOnboardingScheduleCommandValidator()
  {
    // W trybie ad-hoc nie ma dni ani godzin do sprawdzania — cała walidacja dotyczy wyłącznie
    // ścieżki grafiku powtarzalnego.
    When(x => !x.UseAdHoc, () =>
    {
      RuleFor(x => x.Days).NotEmpty().WithMessage("Wybierz przynajmniej jeden dzień pracy.");

      RuleFor(x => x.Days)
        .Must(days => days == null || days.Select(d => d.Day).Distinct().Count() == days.Count)
        .WithMessage("Każdy dzień tygodnia może wystąpić tylko raz.");

      // Godziny walidujemy WYŁĄCZNIE w trybie siatki. W trybie stałym dzień nie ma okna pracy,
      // więc wymaganie zakresu było fikcją — handler i tak go wyrzucał.
      When(x => x.SlotMode == SlotGenerationMode.Grid, () =>
      {
        RuleForEach(x => x.Days).ChildRules(day =>
        {
          day.RuleFor(d => d.StartTime).NotNull()
            .WithMessage("Podaj godzinę rozpoczęcia dla każdego dnia pracy.");
          day.RuleFor(d => d.EndTime).NotNull()
            .WithMessage("Podaj godzinę zakończenia dla każdego dnia pracy.");
          day.RuleFor(d => d.EndTime)
            .GreaterThan(d => d.StartTime)
            .When(d => d.StartTime != null && d.EndTime != null)
            .WithMessage("Godzina zakończenia musi być późniejsza niż rozpoczęcia.");
        });
      });

      When(x => x.SlotMode == SlotGenerationMode.FixedStartTimes, () =>
      {
        RuleFor(x => x.FixedStartTimes).NotEmpty()
          .WithMessage("Podaj przynajmniej jedną godzinę startu.");
      });
    });
  }
}

internal sealed class SetOnboardingScheduleCommandHandler
  : TenantHandler<SetOnboardingScheduleCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly ISender _mediator;
  private readonly TimeProvider _timeProvider;
  private readonly IStaffAccessPolicy _access;

  public SetOnboardingScheduleCommandHandler(
    IApplicationDbContext context,
    ISender mediator,
    TimeProvider timeProvider,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
    _mediator = mediator;
    _timeProvider = timeProvider;
    _access = access;
  }

  public override async Task Handle(SetOnboardingScheduleCommand request, CancellationToken ct)
  {
    // Include(Schedules) jest potrzebny gałęzi ad-hoc, która wycisza istniejące grafiki —
    // bez niego kolekcja byłaby pusta i pętla nie zrobiłaby nic (brak lazy loadingu).
    var owner = await _context.Employees
      .Include(e => e.Schedules)
      .FirstOrDefaultAsync(e => e.UserId != null, ct)
      ?? throw new NotFoundException(nameof(Employee), TenantId);
    var ownerEmployeeId = owner.Id;

    // Autoryzacja MUSI stać przed pierwszym Set* na encji.
    //
    // Dotąd jedyną bramką była delegacja do SetEmployeeScheduleCommand na końcu metody — a gałąź
    // `UseAdHoc` poniżej robi `return` PRZED nią. Pracownik wysyłający {"useAdHoc": true} przestawiał
    // więc cudzy rekord i dezaktywował WSZYSTKIE aktywne grafiki właścicielki: `ResolveScheduleDay`
    // filtruje po IsActive, więc publiczny booking przestawał generować terminy. Cichy DoS na
    // sprzedaż salonu, bez śladu w audycie (preflight 2026-07-31).
    //
    // Sama polityka na kontrolerze tego nie domyka: `BusinessManagement` przepuszcza też rolę Admin,
    // a handler i tak wybiera cel przez `FirstOrDefaultAsync(e => e.UserId != null)` — pierwszy
    // pracownik z kontem, niekoniecznie wołający. Guard w handlerze jest jedynym miejscem, które
    // wiąże wołającego z rekordem, który zaraz zmieni.
    _access.EnsureSelfOrStaffManager(ownerEmployeeId);

    // Papierowy kalendarz: nie tworzymy grafiku — zapisujemy deklarację i kończymy. Silnik
    // dostępności już to obsługuje (wyjątek rozstrzyga bez grafiku), więc nie ma tu nic więcej
    // do zrobienia. Flaga jest jedynym śladem, po którym kreator i panel odróżnią ten tryb od
    // „jeszcze nie ustawiłam grafiku".
    if (request.UseAdHoc)
    {
      owner.SetUsesAdHocSchedule(true);

      // SlotMode zapisujemy TAKŻE tutaj: to nie ustawienie grafiku, tylko podpowiedź startowa dla
      // każdego dnia specjalnego, który właścicielka doda później (patrz `employeeMode`
      // w employee-special-days.component.ts). Bez tego jej wybór z kroku „Jak wyznaczasz terminy?"
      // przepadałby po cichu i każdy dzień startowałby w trybie siatki.
      // Normalnie ustawia to SetEmployeeScheduleCommand — a tej ścieżki tu nie ma.
      owner.SetSlotGenerationMode(request.SlotMode);

      // Wyciszamy istniejące grafiki. Ścieżka jest realna: „powtarzalny" → Dalej (grafik powstaje)
      // → Wstecz → „na bieżąco" → Dalej. Bez tego deklaracja „nie prowadzę grafiku" zostawiałaby
      // stary grafik AKTYWNY, a `ResolveScheduleDay` (filtruje po IsActive) dalej generowałby z niego
      // terminy — czyli salon pokazywałby klientkom Pn–Pt 9–17 mimo deklaracji, że tak nie pracuje.
      //
      // Dezaktywacja, nie kasowanie: powrót na grafik powtarzalny nie ma kosztować utraty ustawień,
      // a SetEmployeeSchedule i tak zakłada wtedy nowy, aktywny grafik.
      foreach (var activeSchedule in owner.Schedules.Where(s => s.IsActive).ToList())
      {
        owner.SetScheduleActive(activeSchedule.Id, false);
      }

      await _context.SaveChangesAsync(ct);
      return;
    }

    // Powrót z papierowego kalendarza do grafiku powtarzalnego (np. przez „Wstecz" w kreatorze).
    owner.SetUsesAdHocSchedule(false);
    await _context.SaveChangesAsync(ct);

    // Tryb siatki → domyślnie wypełnianie luk PreferAdjacent (bez pytania właściciela).
    if (request.SlotMode == SlotGenerationMode.Grid)
    {
      var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct);
      if (tenant != null)
      {
        tenant.Update(
          tenant.Name,
          tenant.Slug,
          gapFillingSettings: new GapFillingSettings(GapFillingMode.PreferAdjacent, 0, 1));
        await _context.SaveChangesAsync(ct);
      }
    }

    var isFixed = request.SlotMode == SlotGenerationMode.FixedStartTimes;
    var fixedTimes = request.FixedStartTimes ?? Array.Empty<TimeOnly>();

    // Godziny per dzień: każdy dzień niesie własny zakres. W trybie stałym dzień nie ma okna pracy
    // (puste WorkRanges) — liczą się wyłącznie godziny startu, wspólne dla wszystkich dni.
    var days = request.Days
      .Select(d => isFixed
        ? new EmployeeScheduleDayDto(
            (int)d.Day, Array.Empty<TimeRangeDto>(), Array.Empty<TimeRangeDto>(), fixedTimes)
        : new EmployeeScheduleDayDto(
            (int)d.Day,
            new[] { new TimeRangeDto(d.StartTime!.Value, d.EndTime!.Value) },
            Array.Empty<TimeRangeDto>(),
            null))
      .ToList();

    // Krok kreatora jest IDEMPOTENTNY: powrót i poprawka godzin ma ZAKTUALIZOWAĆ istniejący grafik,
    // nie dołożyć drugi. Bez tego Id `SetEmployeeSchedule` woła AddSchedule, które rzuca
    // SchedulesCollision, gdy aktywny zakres nachodzi na istniejący — a kreator zawsze zakłada
    // „od dziś bezterminowo", więc drugie przejście kroku kolidowało SAMO ZE SOBĄ
    // („Dalej → Wstecz → popraw godziny → Dalej" = serwer odrzuca żądanie).
    //
    // Bierzemy też grafik nieaktywny: gałąź ad-hoc go wycisza, więc powrót na powtarzalny ma go
    // reaktywować (UpdateSchedule ustawia IsActive z DTO), a nie zostawiać sierotę obok nowego.
    var existingScheduleId =
      (owner.Schedules.FirstOrDefault(s => s.IsActive) ?? owner.Schedules.FirstOrDefault())?.Id;

    // ActiveFrom musi być realną datą — walidator SetEmployeeSchedule odrzuca default(DateOnly)
    // (Must(NotDefaultDate)). Grafik ma obowiązywać od dziś bezterminowo (jak DemoDataSeeder).
    var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
    var schedule = new EmployeeScheduleDto(
      ActiveFrom: today,
      ActiveTo: DateOnly.MaxValue,
      NumberOfCycles: 1,
      Days: days,
      Id: existingScheduleId,
      SlotGenerationMode: request.SlotMode);

    await _mediator.Send(new SetEmployeeScheduleCommand(ownerEmployeeId, schedule), ct);
  }
}
