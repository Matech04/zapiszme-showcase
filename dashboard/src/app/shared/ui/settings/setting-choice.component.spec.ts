import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach } from 'vitest';
import { SettingChoiceComponent, type SettingChoiceOption } from './setting-choice.component';

@Component({
  standalone: true,
  imports: [SettingChoiceComponent],
  template: `
    <app-setting-choice
      label="Dostęp"
      [options]="options"
      [value]="value()"
      (valueChange)="value.set($event)"
    />
  `,
})
class HostComponent {
  readonly options: readonly SettingChoiceOption[] = [
    { value: 'open', title: 'Otwarte', description: 'Każdy', testId: 'opt-open' },
    { value: 'invite_only', title: 'Zaproszeni', description: 'Whitelista', testId: 'opt-invite' },
  ];
  readonly value = signal<unknown>('open');
}

describe('SettingChoiceComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renderuje wszystkie opcje', () => {
    const buttons = fixture.debugElement.queryAll(By.css('button'));
    expect(buttons.length).toBe(2);
    expect(buttons[0].nativeElement.textContent).toContain('Otwarte');
    expect(buttons[1].nativeElement.textContent).toContain('Zaproszeni');
  });

  it('zaznacza aktywną opcję (border-amber-500)', () => {
    const active = fixture.debugElement.query(By.css('[data-testid="opt-open"]'));
    expect(active.nativeElement.className).toContain('border-amber-500');
    const inactive = fixture.debugElement.query(By.css('[data-testid="opt-invite"]'));
    expect(inactive.nativeElement.className).not.toContain('border-amber-500');
  });

  it('klik opcji emituje valueChange z jej wartością', () => {
    const invite = fixture.debugElement.query(By.css('[data-testid="opt-invite"]'));
    invite.nativeElement.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.value()).toBe('invite_only');
  });
});
