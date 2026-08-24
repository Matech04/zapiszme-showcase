import { describe, it, expect, beforeEach, vi } from 'vitest';
import { CappedMessageService } from './capped-message.service';

/**
 * Limit toastów jest wymuszany w usłudze, bo `<p-toast>` (PrimeNG 21) nie ma takiego wejścia.
 * Kluczowe: nadmiar NIE ginie — czeka w kolejce i wchodzi, gdy `release()` zwolni slot.
 */
describe('CappedMessageService', () => {
  let service: CappedMessageService;
  let emitted: string[];

  const msg = (detail: string) => ({ severity: 'info', summary: 's', detail });

  beforeEach(() => {
    service = new CappedMessageService();
    emitted = [];
    service.messageObserver.subscribe((m) => {
      const one = Array.isArray(m) ? m[0] : m;
      emitted.push(one.detail as string);
    });
  });

  it('przepuszcza pierwsze trzy komunikaty od razu', () => {
    ['a', 'b', 'c'].forEach((d) => service.add(msg(d)));

    expect(emitted).toEqual(['a', 'b', 'c']);
    expect(service.visibleCount).toBe(3);
    expect(service.pendingCount).toBe(0);
  });

  it('czwarty komunikat czeka w kolejce, nie ginie', () => {
    ['a', 'b', 'c', 'd'].forEach((d) => service.add(msg(d)));

    expect(emitted).toEqual(['a', 'b', 'c']);
    expect(service.pendingCount).toBe(1);

    // Toast zniknął (auto albo klik) → zwalnia się slot.
    service.release();

    expect(emitted).toEqual(['a', 'b', 'c', 'd']);
    expect(service.visibleCount).toBe(3);
    expect(service.pendingCount).toBe(0);
  });

  it('release bez kolejki tylko zmniejsza licznik i nie schodzi poniżej zera', () => {
    service.add(msg('a'));
    service.release();
    expect(service.visibleCount).toBe(0);

    service.release();
    expect(service.visibleCount).toBe(0);
    expect(emitted).toEqual(['a']);
  });

  it('lawina komunikatów nie rośnie w nieskończoność — kolejka jest ograniczona', () => {
    // 3 widoczne + 10 w kolejce; starsze oczekujące odpadają.
    for (let i = 0; i < 40; i++) service.add(msg(`m${i}`));

    expect(service.visibleCount).toBe(3);
    expect(service.pendingCount).toBe(10);

    // W kolejce zostają NAJŚWIEŻSZE (m30..m39): świeży błąd jest istotniejszy niż stary.
    for (let i = 0; i < 10; i++) service.release();
    expect(emitted.slice(3)).toEqual(
      Array.from({ length: 10 }, (_, i) => `m${30 + i}`),
    );
  });

  it('addAll przechodzi przez ten sam limit', () => {
    service.addAll([msg('a'), msg('b'), msg('c'), msg('d')]);

    expect(emitted).toEqual(['a', 'b', 'c']);
    expect(service.pendingCount).toBe(1);
  });

  it('clear zeruje licznik i kolejkę', () => {
    ['a', 'b', 'c', 'd'].forEach((d) => service.add(msg(d)));
    service.clear();

    expect(service.visibleCount).toBe(0);
    expect(service.pendingCount).toBe(0);

    // Po wyczyszczeniu znów mamy pełne trzy sloty.
    service.add(msg('e'));
    expect(service.visibleCount).toBe(1);
  });

  it('nie gubi wiadomości przy naprzemiennym add/release', () => {
    const spy = vi.spyOn(service, 'release');
    ['a', 'b', 'c'].forEach((d) => service.add(msg(d)));
    service.add(msg('d'));
    service.release();
    service.add(msg('e'));
    service.release();
    service.release();

    expect(emitted).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(spy).toHaveBeenCalledTimes(3);
  });
});
