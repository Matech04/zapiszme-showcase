import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { AgendaTileComponent } from './agenda-tile.component';

@Component({
  standalone: true,
  imports: [AgendaTileComponent],
  template: `
    <ul>
      <li
        appAgendaTile
        [startLabel]="startLabel"
        [endLabel]="endLabel"
        [barClass]="barClass"
        [clickable]="clickable"
        [selected]="selected"
        [ariaLabel]="ariaLabel"
        (activate)="onActivate()"
      >
        <span class="proj">treść</span>
      </li>
    </ul>
  `,
})
class HostComponent {
  startLabel = '09:00';
  endLabel: string | null = '09:30';
  barClass = 'bg-sky-500';
  clickable = false;
  selected = false;
  ariaLabel: string | null = null;
  events = 0;
  onActivate(): void {
    this.events++;
  }
}

describe('AgendaTileComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
  });

  const li = (): HTMLElement => fixture.nativeElement.querySelector('li');

  it('renderuje godzinę startu, końca oraz rzutowaną treść', () => {
    fixture.detectChanges();
    expect(li().textContent).toContain('09:00');
    expect(li().textContent).toContain('09:30');
    expect(li().querySelector('.proj')).toBeTruthy();
  });

  it('pomija drugą godzinę gdy endLabel = null (znacznik pracy)', () => {
    host.endLabel = null;
    fixture.detectChanges();
    expect(li().textContent).not.toContain('09:30');
  });

  it('nakłada klasę paska statusu na pasek', () => {
    fixture.detectChanges();
    expect(li().querySelector('.bg-sky-500')).toBeTruthy();
  });

  it('clickable=false: brak role/tabindex, klik nie emituje activate', () => {
    fixture.detectChanges();
    expect(li().getAttribute('role')).toBeNull();
    expect(li().getAttribute('tabindex')).toBeNull();
    li().click();
    expect(host.events).toBe(0);
  });

  it('clickable=true: role=button, tabindex=0, klik emituje activate', () => {
    host.clickable = true;
    fixture.detectChanges();
    expect(li().getAttribute('role')).toBe('button');
    expect(li().getAttribute('tabindex')).toBe('0');
    li().click();
    expect(host.events).toBe(1);
  });

  it('selected=true nakłada ring podświetlenia', () => {
    host.selected = true;
    fixture.detectChanges();
    expect(li().classList.contains('ring-2')).toBe(true);
  });
});
