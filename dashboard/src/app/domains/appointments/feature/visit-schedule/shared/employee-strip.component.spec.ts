import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach } from 'vitest';
import { EmployeeStripComponent, initialsOf } from './employee-strip.component';

describe('initialsOf', () => {
  it.each([
    ['Jan Nowak', 'JN'],
    ['anna kowalska', 'AK'],
    ['Piotr', 'P'],
    ['Maria Anna Wiśniewska', 'MW'],
  ])('„%s" → „%s"', (label, expected) => {
    expect(initialsOf(label)).toBe(expected);
  });

  it('pusta etykieta nie wysypuje się', () => {
    expect(initialsOf('   ')).toBe('?');
  });
});

/**
 * Pasek odpowiada wyłącznie na pytanie „czyj kalendarz oglądamy" — nie jest filtrem wizyt,
 * więc nie ma chipa „Wszyscy" ani wyboru wielokrotnego.
 */
describe('EmployeeStripComponent', () => {
  let fixture: ComponentFixture<EmployeeStripComponent>;
  let component: EmployeeStripComponent;

  const employees = [
    { id: 'e1', label: 'Jan Nowak' },
    { id: 'e2', label: 'Piotr Wiśniewski' },
  ];

  const chips = () =>
    fixture.debugElement.queryAll(By.css('button')).map((b) => b.nativeElement as HTMLButtonElement);

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [EmployeeStripComponent] }).compileComponents();
    fixture = TestBed.createComponent(EmployeeStripComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employees', employees);
    fixture.componentRef.setInput('selectedId', null);
    fixture.detectChanges();
  });

  it('renderuje po jednym chipie na pracownika — bez chipa „Wszyscy"', () => {
    expect(chips()).toHaveLength(2);
    expect(chips().map((c) => c.textContent)).not.toContainEqual(expect.stringContaining('Wszyscy'));
  });

  it('wybrany pracownik → jego chip aktywny, pozostałe nie', () => {
    fixture.componentRef.setInput('selectedId', 'e2');
    fixture.detectChanges();
    expect(chips()[0].getAttribute('aria-pressed')).toBe('false');
    expect(chips()[1].getAttribute('aria-pressed')).toBe('true');
  });

  it('brak wyboru → żaden chip nie jest aktywny', () => {
    expect(chips().every((c) => c.getAttribute('aria-pressed') === 'false')).toBe(true);
  });

  it('klik w chip emituje select z id pracownika', () => {
    let emitted: string | undefined;
    component.select.subscribe((id) => (emitted = id));
    chips()[0].click();
    expect(emitted).toBe('e1');
  });

  it('pokazuje inicjały pracownika w awatarze', () => {
    expect(chips()[0].textContent).toContain('JN');
    expect(chips()[1].textContent).toContain('PW');
  });
});
