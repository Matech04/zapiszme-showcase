import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { of } from 'rxjs';
import { ConfirmationService, MessageService } from 'primeng/api';
import { CappedMessageService } from '@core/services/capped-message.service';
import { AuthClient, DemoClient, SalonSettingsClient } from '@core/api/api-client';

import { App } from './app';

// App template: <router-outlet> + <p-toast> + <p-confirmDialog>.
// Pośrednio inicjalizuje SalonTitleService → AuthSessionService → AuthClient.me().
// Stubujemy AuthClient żeby nie potrzebować realnego HTTP backendu.
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideRouter([]),
        // App wstrzykuje CappedMessageService, by zwalniać sloty limitu na (onClose).
        CappedMessageService,
        { provide: MessageService, useExisting: CappedMessageService },
        ConfirmationService,
        {
          provide: AuthClient,
          useValue: { me: () => of(null) },
        },
        {
          provide: SalonSettingsClient,
          useValue: { get: () => of(null) },
        },
        {
          // AuthSessionService wstrzykuje DemoClient (tryb demo) — stub żeby DI się rozwiązało.
          provide: DemoClient,
          useValue: { enabled: () => of({ enabled: false }), start: () => of(null) },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render without throwing on detectChanges', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    // smoke — komponent montuje się bez exception. Brak konkretnego h1 do sprawdzenia
    // bo App.template to tylko <router-outlet> + dialogs PrimeNG.
    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });
});
