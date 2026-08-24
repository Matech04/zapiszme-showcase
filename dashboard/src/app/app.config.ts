import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { ConfirmationService, MessageService } from 'primeng/api';
import { CappedMessageService } from '@core/services/capped-message.service';
import { DialogService } from 'primeng/dynamicdialog';
import { errorInterceptor } from '@core/interceptors/error.interceptor';
import { httpCacheInterceptor } from '@core/interceptors/http-cache.interceptor';
import { clientModeInterceptor } from '@core/interceptors/client-mode.interceptor';
import { credentialsInterceptor } from '@core/interceptors/credentials.interceptor';
import { xsrfInterceptor } from '@core/interceptors/xsrf.interceptor';
import {
  AdminImpersonationClient,
  AdminMaintenanceClient,
  AdminPromoCodesClient,
  AdminSmsTemplatesClient,
  API_BASE_URL,
  AppointmentsClient,
  AuthClient,
  CustomersClient,
  DemoClient,
  DepositsClient,
  EmployeesClient,
  FeedbackClient,
  GuidesClient,
  NotificationsClient,
  OnboardingClient,
  PublicPromoClient,
  SalonSettingsClient,
  ServiceCategoriesClient,
  ServicesClient,
  ShiftTemplatesClient,
  SmsTemplatesClient,
  SubscriptionClient,
  TenantsClient,
  VatRatesClient,
} from '@core/api/api-client';
import { ZapiszMeTheme } from '@shared/ui/ThemePreset';
import { environment } from '@env/environment';
import { DashboardThemeService } from '@core/theme/dashboard-theme.service';
import { ThemeModeService } from '@core/theme/theme-mode.service';
import { PwaUpdateService } from '@core/pwa/pwa-update.service';
import { PwaInstallService } from '@core/pwa/pwa-install.service';
import { PushNotificationsService } from '@core/services/push-notifications.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(() => {
      inject(DashboardThemeService).hydrateFromStorage();
      inject(ThemeModeService).hydrate();
      inject(PwaUpdateService).init();
      inject(PwaInstallService).init();
      inject(PushNotificationsService).init();
    }),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    providePrimeNG({
      theme: {
        preset: ZapiszMeTheme,
        options: {
          darkModeSelector: '.dark',
          cssLayer: {
            name: 'primeng',
            order: 'theme, base, primeng',
          },
        },
      },
      // Polska lokalizacja datepickerów (nazwy miesięcy/dni, tydzień od poniedziałku).
      translation: {
        firstDayOfWeek: 1,
        dayNames: ['niedziela', 'poniedziałek', 'wtorek', 'środa', 'czwartek', 'piątek', 'sobota'],
        dayNamesShort: ['niedz.', 'pon.', 'wt.', 'śr.', 'czw.', 'pt.', 'sob.'],
        dayNamesMin: ['N', 'Pn', 'Wt', 'Śr', 'Cz', 'Pt', 'So'],
        monthNames: [
          'styczeń', 'luty', 'marzec', 'kwiecień', 'maj', 'czerwiec',
          'lipiec', 'sierpień', 'wrzesień', 'październik', 'listopad', 'grudzień',
        ],
        monthNamesShort: ['sty', 'lut', 'mar', 'kwi', 'maj', 'cze', 'lip', 'sie', 'wrz', 'paź', 'lis', 'gru'],
        today: 'Dziś',
        clear: 'Wyczyść',
        dateFormat: 'dd.mm.yy',
        weekHeader: 'Tydz.',
      },
    }),
    provideHttpClient(
      withFetch(),
      // httpCacheInterceptor JAKO OSTATNI — leży najbliżej sieci, więc deduplikuje realne
      // round-tripy, a pozostałe interceptory (cookies, CSRF, mapowanie błędów) dalej widzą
      // każde logiczne żądanie osobno.
      withInterceptors([
        credentialsInterceptor,
        clientModeInterceptor,
        xsrfInterceptor,
        errorInterceptor,
        httpCacheInterceptor,
      ]),
    ),
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
    ConfirmationService,
    // Limit jednocześnie widocznych toastów. `useExisting` — cały kod wstrzykuje `MessageService`
    // i nic o nadpisaniu nie wie; `App` sięga po `CappedMessageService`, by zwalniać sloty.
    CappedMessageService,
    { provide: MessageService, useExisting: CappedMessageService },
    DialogService,
    AuthClient,
    DemoClient,
    DepositsClient,
    EmployeesClient,
    AppointmentsClient,
    CustomersClient,
    ServiceCategoriesClient,
    ServicesClient,
    SalonSettingsClient,
    ShiftTemplatesClient,
    VatRatesClient,
    FeedbackClient,
    NotificationsClient,
    OnboardingClient,
    GuidesClient,
    SubscriptionClient,
    TenantsClient,
    PublicPromoClient,
    AdminPromoCodesClient,
    AdminImpersonationClient,
    AdminMaintenanceClient,
    SmsTemplatesClient,
    AdminSmsTemplatesClient,
  ],
};
