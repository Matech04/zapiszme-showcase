import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { PwaInstallService } from '@core/pwa/pwa-install.service';
import { PushNotificationsService } from '@core/services/push-notifications.service';
import { BackButtonCloseService } from '@core/services/back-button-close.service';
import { MoreSheetComponent } from './more-sheet.component';

/**
 * Regresja: „przyciemnienie na całym ekranie po wylogowaniu". PrimeNG trzyma maskę drawera
 * w <body> i usuwa ją dopiero po animacji wyjścia; nawigacja na /login niszczy layout szybciej,
 * a Drawer.onDestroy sprząta maskę tylko gdy `visible` jest wciąż true.
 */
describe('MoreSheetComponent — wylogowanie z otwartego arkusza', () => {
  let fixture: ComponentFixture<MoreSheetComponent>;
  let component: MoreSheetComponent;
  let auth: { logout: any; session: any; isDemo: any };
  let backClose: BackButtonCloseService;

  beforeEach(async () => {
    auth = {
      logout: vi.fn(),
      session: signal({ displayName: 'Ala', email: 'ala@example.com' }),
      isDemo: signal(false),
    };

    await TestBed.configureTestingModule({
      imports: [MoreSheetComponent],
      providers: [
        provideRouter([]),
        { provide: AuthSessionService, useValue: auth },
        {
          provide: PwaInstallService,
          useValue: {
            available: signal(false),
            canInstall: () => false,
            iosInstructions: () => false,
          },
        },
        {
          provide: PushNotificationsService,
          useValue: {
            supported: signal(false),
            iosNeedsInstall: signal(false),
            subscribed: signal(false),
            busy: signal(false),
          },
        },
      ],
    }).compileComponents();

    backClose = TestBed.inject(BackButtonCloseService);
    fixture = TestBed.createComponent(MoreSheetComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('groups', []);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('nie zamyka arkusza przed nawigacją — inaczej maska zostaje osierocona w <body>', () => {
    component.logout();

    expect(auth.logout).toHaveBeenCalled();
    expect(component.visible()).toBe(true);
  });

  it('po zniszczeniu komponentu z otwartym arkuszem nie zostaje maska ani blokada scrolla', async () => {
    component.logout();
    fixture.destroy();
    await fixture.whenStable();

    expect(document.querySelectorAll('.p-overlay-mask').length).toBe(0);
    expect(document.body.style.overflow).not.toBe('hidden');
  });

  it('zdejmuje handler „wstecz" przy zniszczeniu, żeby nie zjadł cofnięcia na /login', () => {
    const pop = vi.spyOn(backClose, 'pop');

    component.logout();
    fixture.destroy();

    expect(pop).toHaveBeenCalled();
  });
});
