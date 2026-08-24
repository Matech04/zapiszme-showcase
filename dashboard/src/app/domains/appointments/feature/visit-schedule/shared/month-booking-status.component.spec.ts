import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { MonthBookingStatusComponent } from './month-booking-status.component';

describe('MonthBookingStatusComponent', () => {
  let fixture: ComponentFixture<MonthBookingStatusComponent>;

  // Zegar wstrzyknięty, żeby test nie rotował razem z kalendarzem.
  const TODAY = new Date(2026, 7, 20); // 20 sierpnia 2026

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MonthBookingStatusComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(MonthBookingStatusComponent);
  });

  function setInputs(partial: {
    opensOn?: string | null;
    hasPublication?: boolean;
    canEdit?: boolean;
  }): void {
    fixture.componentRef.setInput('today', TODAY);
    fixture.componentRef.setInput('opensOn', partial.opensOn ?? null);
    fixture.componentRef.setInput('hasPublication', partial.hasPublication ?? false);
    fixture.componentRef.setInput('canEdit', partial.canEdit ?? true);
    fixture.detectChanges();
  }

  const html = () => fixture.nativeElement.textContent as string;

  it('bez wpisu publikacji miesiąc jest domyślnie otwarty', () => {
    setInputs({ hasPublication: false });

    expect(fixture.componentInstance.state()).toBe('default');
    expect(html()).toContain('Zapisy otwarte');
  });

  it('wpis z przyszłą datą pokazuje termin otwarcia', () => {
    setInputs({ hasPublication: true, opensOn: '2026-09-01' });

    expect(fixture.componentInstance.state()).toBe('closedUntil');
    expect(html()).toContain('1 września');
    expect(
      fixture.nativeElement.querySelector('[data-testid="month-booking-status-closed"]'),
    ).toBeTruthy();
  });

  it('wpis bez daty to zamknięcie bezterminowe — bez obietnicy terminu', () => {
    setInputs({ hasPublication: true, opensOn: null });

    expect(fixture.componentInstance.state()).toBe('closedIndefinitely');
    expect(html()).toContain('Zapisy zamknięte dla klientów');
  });

  it('wpis z datą, która już minęła, znaczy otwarte', () => {
    setInputs({ hasPublication: true, opensOn: '2026-08-01' });

    expect(fixture.componentInstance.state()).toBe('opened');
    expect(html()).toContain('Zapisy otwarte');
  });

  it('data otwarcia równa dzisiaj otwiera miesiąc (granica domknięta)', () => {
    setInputs({ hasPublication: true, opensOn: '2026-08-20' });

    expect(fixture.componentInstance.state()).toBe('opened');
  });

  it('bez uprawnień nie pokazuje przycisku edycji', () => {
    setInputs({ hasPublication: true, opensOn: '2026-09-01', canEdit: false });

    expect(
      fixture.nativeElement.querySelector('[data-testid="month-booking-status-edit"]'),
    ).toBeNull();
    // Sam komunikat o zamknięciu zostaje — pracownik ma widzieć, że klienci nie rezerwują.
    expect(html()).toContain('Zapisy zamknięte');
  });

  it('bez uprawnień i przy otwartym miesiącu pasek w ogóle nie zajmuje miejsca', () => {
    setInputs({ hasPublication: false, canEdit: false });

    expect(html().trim()).toBe('');
  });
});
