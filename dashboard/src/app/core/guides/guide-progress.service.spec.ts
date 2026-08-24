import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GuidesClient } from '@core/api/api-client';
import { GuideProgressService } from './guide-progress.service';

/**
 * Postęp przewodników mieszka w bazie (per użytkownik), a UI ma reagować natychmiast.
 * Te testy pilnują obu stron tego kompromisu: optymistycznego zapisu i cofnięcia go,
 * gdy serwer odmówi — inaczej katalog pokazywałby „Ukończono" dla czegoś, czego nie zapisano.
 */
describe('GuideProgressService', () => {
  let client: {
    getCompletions: ReturnType<typeof vi.fn>;
    markCompleted: ReturnType<typeof vi.fn>;
    resetCompletion: ReturnType<typeof vi.fn>;
  };

  function setup(): GuideProgressService {
    TestBed.configureTestingModule({
      providers: [GuideProgressService, { provide: GuidesClient, useValue: client }],
    });
    return TestBed.inject(GuideProgressService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    client = {
      getCompletions: vi.fn().mockReturnValue(of(['set-weekly-schedule'])),
      markCompleted: vi.fn().mockReturnValue(of(null)),
      resetCompletion: vi.fn().mockReturnValue(of(null)),
    };
  });

  it('ładuje postęp z serwera', async () => {
    const service = setup();
    await service.load();

    expect(service.isCompleted('set-weekly-schedule')).toBe(true);
    expect(service.isCompleted('add-service')).toBe(false);
    expect(service.isLoaded()).toBe(true);
  });

  it('równoległe wywołania load dzielą jedno żądanie', async () => {
    const service = setup();
    await Promise.all([service.load(), service.load(), service.load()]);

    expect(client.getCompletions).toHaveBeenCalledTimes(1);
  });

  it('markCompleted odhacza od razu, przed odpowiedzią serwera', async () => {
    const service = setup();
    await service.load();

    const pending = service.markCompleted('add-service');
    expect(service.isCompleted('add-service')).toBe(true);

    await pending;
    expect(client.markCompleted).toHaveBeenCalledWith('add-service');
  });

  it('nieudany zapis cofa optymistyczne odhaczenie', async () => {
    client.markCompleted = vi.fn().mockReturnValue(throwError(() => new Error('offline')));
    const service = setup();
    await service.load();

    await service.markCompleted('add-service');

    expect(service.isCompleted('add-service')).toBe(false);
  });

  it('nieudany reset przywraca znacznik', async () => {
    client.resetCompletion = vi.fn().mockReturnValue(throwError(() => new Error('offline')));
    const service = setup();
    await service.load();

    await service.reset('set-weekly-schedule');

    expect(service.isCompleted('set-weekly-schedule')).toBe(true);
  });

  it('błąd odczytu nie wywraca panelu i pozwala spróbować ponownie', async () => {
    client.getCompletions = vi.fn().mockReturnValue(throwError(() => new Error('offline')));
    const service = setup();

    await service.load();

    expect(service.isLoaded()).toBe(false);
    expect(service.isCompleted('set-weekly-schedule')).toBe(false);

    // Kolejne wejście do katalogu ma ponowić próbę, a nie utrwalić pusty stan.
    client.getCompletions = vi.fn().mockReturnValue(of(['add-service']));
    await service.load();
    expect(service.isCompleted('add-service')).toBe(true);
  });

  it('powtórne markCompleted nie generuje drugiego żądania', async () => {
    const service = setup();
    await service.load();

    await service.markCompleted('set-weekly-schedule');

    expect(client.markCompleted).not.toHaveBeenCalled();
  });
});
