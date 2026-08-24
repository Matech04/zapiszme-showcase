import { DOCUMENT, Injectable, computed, inject, isDevMode, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { MessageService } from 'primeng/api';
import { firstValueFrom } from 'rxjs';
import { NotificationsClient } from '@core/api/api-client';

/**
 * Web Push dla panelu (PWA). Rejestruje/kasuje subskrypcję urządzenia przez SwPush i backend,
 * obsługuje kliknięcie powiadomienia (deep-link do wizyty). Push wymaga produkcyjnego builda
 * (SW włączony) i zaufanego HTTPS. iOS: działa TYLKO w PWA dodanej do ekranu głównego (16.4+),
 * więc w Safari-karcie na iOS pokazujemy instrukcję instalacji zamiast prośby o zgodę.
 */
@Injectable({ providedIn: 'root' })
export class PushNotificationsService {
  private readonly swPush = inject(SwPush);
  private readonly api = inject(NotificationsClient);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);
  private readonly document = inject(DOCUMENT);

  private readonly _subscribed = signal(false);
  private readonly _busy = signal(false);

  /** Czy to urządzenie ma aktywną subskrypcję push. */
  readonly subscribed = this._subscribed.asReadonly();
  readonly busy = this._busy.asReadonly();

  /** SW aktywny (tylko produkcyjny build) + Push API dostępne w tej przeglądarce. */
  readonly supported = computed(() => this.swPush.isEnabled && this.hasPushApi());

  /** iOS bez zainstalowanej PWA — push wymaga „Dodaj do ekranu głównego". */
  readonly iosNeedsInstall = computed(() => this.isIos() && !this.isStandalone());

  init(): void {
    if (isDevMode()) {
      // W dev SW jest wyłączony, więc realny push nie dotrze. Udostępniamy w konsoli
      // `__previewPushNotification()`, by zobaczyć jak wygląda samo powiadomienie (renderuje je
      // service worker z prod-builda obecny na localhost — patrz komentarz w metodzie).
      const win = this.document.defaultView as
        | (Window & { __previewPushNotification?: () => void })
        | null;
      if (win) {
        win.__previewPushNotification = () => void this.previewNotification();
      }
    }

    if (!this.swPush.isEnabled) {
      return;
    }

    // Hydratacja stanu z aktualnej subskrypcji przeglądarki.
    this.swPush.subscription.subscribe((sub) => this._subscribed.set(!!sub));

    // Klik w powiadomienie → deep-link (ngsw payload: data.onActionClick.default.url).
    this.swPush.notificationClicks.subscribe(({ notification }) => {
      const url = (notification as { data?: { onActionClick?: { default?: { url?: string } } } })
        ?.data?.onActionClick?.default?.url;
      if (url) {
        void this.router.navigateByUrl(url);
      }
    });
  }

  async enable(): Promise<void> {
    if (this.iosNeedsInstall()) {
      this.messages.add({
        severity: 'info',
        summary: 'Zainstaluj aplikację',
        detail:
          'Na iPhonie powiadomienia działają w zainstalowanej aplikacji. Otwórz menu Udostępnij i wybierz „Dodaj do ekranu głównego", a potem włącz powiadomienia z poziomu aplikacji.',
        life: 8000,
      });
      return;
    }

    if (!this.supported()) {
      this.messages.add({
        severity: 'warn',
        summary: 'Powiadomienia niedostępne',
        detail: 'Ta przeglądarka nie obsługuje powiadomień push lub aplikacja nie jest w trybie produkcyjnym.',
        life: 6000,
      });
      return;
    }

    this._busy.set(true);
    try {
      const vapid = await firstValueFrom(this.api.getVapidPublicKey());
      if (!vapid?.enabled || !vapid.publicKey) {
        this.messages.add({
          severity: 'warn',
          summary: 'Powiadomienia niedostępne',
          detail: 'Serwer nie ma skonfigurowanych powiadomień push.',
          life: 6000,
        });
        return;
      }

      const sub = await this.swPush.requestSubscription({ serverPublicKey: vapid.publicKey });
      const json = sub.toJSON();
      await firstValueFrom(
        this.api.subscribePush({
          endpoint: sub.endpoint,
          p256dh: json.keys?.['p256dh'],
          auth: json.keys?.['auth'],
        }),
      );

      this._subscribed.set(true);
      this.messages.add({
        severity: 'success',
        summary: 'Powiadomienia włączone',
        detail: 'To urządzenie będzie dostawać powiadomienia z panelu.',
        life: 4000,
      });
    } catch (err) {
      // Odmowa zgody lub błąd subskrypcji — nie pokazujemy technicznego błędu.
      this.messages.add({
        severity: 'warn',
        summary: 'Nie włączono powiadomień',
        detail: 'Zgoda na powiadomienia została odrzucona lub subskrypcja się nie powiodła.',
        life: 6000,
      });
    } finally {
      this._busy.set(false);
    }
  }

  async disable(): Promise<void> {
    if (!this.swPush.isEnabled) {
      return;
    }

    this._busy.set(true);
    try {
      const sub = await firstValueFrom(this.swPush.subscription);
      if (sub) {
        await firstValueFrom(this.api.unsubscribePush({ endpoint: sub.endpoint }));
        await this.swPush.unsubscribe();
      }
      this._subscribed.set(false);
      this.messages.add({
        severity: 'success',
        summary: 'Powiadomienia wyłączone',
        detail: 'To urządzenie nie będzie już dostawać powiadomień push.',
        life: 4000,
      });
    } catch {
      this.messages.add({
        severity: 'warn',
        summary: 'Nie udało się wyłączyć',
        detail: 'Spróbuj ponownie za chwilę.',
        life: 5000,
      });
    } finally {
      this._busy.set(false);
    }
  }

  /**
   * DEV-only: pokazuje przykładowe powiadomienie o wyglądzie zbliżonym do realnego pusha.
   * Jeśli na origin jest zarejestrowany service worker (np. z prod-builda na localhost) — renderujemy
   * przez `registration.showNotification` (tą samą ścieżką co ngsw), więc widać ikonę/badge/treść jak
   * na produkcji. W przeciwnym razie fallback do `new Notification`. Poza dev nie istnieje.
   */
  private async previewNotification(): Promise<void> {
    const win = this.document.defaultView;
    if (!win || !('Notification' in win)) {
      return;
    }
    if (win.Notification.permission !== 'granted') {
      const permission = await win.Notification.requestPermission();
      if (permission !== 'granted') {
        return;
      }
    }

    const title = 'Nowa rezerwacja';
    const options: NotificationOptions = {
      body: 'Anna Kowalska — Strzyżenie damskie, dziś 14:30',
      icon: '/icons/icon-192.png',
      badge: '/icons/icon-192.png',
      data: { onActionClick: { default: { url: '/admin' } } },
    };

    const registration = await win.navigator.serviceWorker?.getRegistration();
    if (registration) {
      await registration.showNotification(title, options);
    } else {
      new win.Notification(title, options);
    }
  }

  private hasPushApi(): boolean {
    const win = this.document.defaultView;
    return !!win && 'Notification' in win && 'PushManager' in win;
  }

  private isIos(): boolean {
    const nav = this.document.defaultView?.navigator;
    if (!nav) return false;
    return (
      /iPad|iPhone|iPod/.test(nav.userAgent) ||
      // iPadOS raportuje się jako Mac z ekranem dotykowym.
      (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1)
    );
  }

  private isStandalone(): boolean {
    const win = this.document.defaultView;
    if (!win) return false;
    return (
      win.matchMedia?.('(display-mode: standalone)').matches ||
      (win.navigator as unknown as { standalone?: boolean }).standalone === true
    );
  }
}
