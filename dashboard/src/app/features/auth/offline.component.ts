import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { fromEvent } from 'rxjs';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { OFFLINE_RETURN_URL_PARAM } from '@core/auth/offline-route';
import { defaultAdminRouteForRole } from '@core/auth/default-admin-route';

/**
 * Ekran pokazywany, gdy sonda sesji nie doleciała do API (offline / API nie odpowiada).
 *
 * Świadomie NIE jest to `/login`: użytkownik nie został wylogowany, więc odsyłanie go do formularza
 * (który bez sieci i tak nie zadziała) tylko utrwalałoby zgłoszenie „wylogowuje mnie cały czas".
 * Po odzyskaniu sieci wracamy dokładnie tam, gdzie użytkownik zmierzał.
 */
@Component({
  selector: 'app-offline',
  standalone: true,
  template: `
    <div class="min-h-dvh relative flex items-center justify-center px-6 py-10 text-center">
      <div class="pointer-events-none absolute inset-0 overflow-hidden">
        <div class="absolute -left-24 top-16 h-72 w-72 rounded-full bg-amber-300/30 blur-3xl"></div>
        <div class="absolute right-0 top-0 h-96 w-96 rounded-full bg-slate-900/15 dark:bg-violet-500/15 blur-3xl"></div>
      </div>
      <section class="admin-glass-card relative z-10 w-full max-w-md rounded-4xl p-8 space-y-4">
        <div
          class="mx-auto grid size-14 place-items-center rounded-full bg-amber-100 text-amber-600 dark:bg-amber-900/40 dark:text-amber-300"
        >
          <i class="pi pi-wifi text-xl"></i>
        </div>
        <p class="admin-section-label text-primary">Połączenie</p>
        <h1 class="text-2xl font-black tracking-tight">Brak połączenia z serwerem</h1>
        <p class="text-surface-600 dark:text-surface-300 max-w-md text-sm leading-relaxed">
          Nie udało się pobrać danych salonu. <strong>Nie zostałaś wylogowana</strong> — Twoja sesja jest
          aktywna, wystarczy odzyskać połączenie.
        </p>
        <button
          type="button"
          [disabled]="retrying()"
          (click)="retry()"
          class="inline-flex items-center gap-2 px-6 py-2.5 rounded-full bg-primary text-primary-contrast font-semibold disabled:opacity-60"
        >
          @if (retrying()) {
            <i class="pi pi-spin pi-spinner"></i>
            <span>Łączę…</span>
          } @else {
            <span>Spróbuj ponownie</span>
          }
        </button>
      </section>
    </div>
  `,
})
export class OfflineComponent implements OnInit {
  private readonly auth = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly retrying = signal(false);

  ngOnInit(): void {
    // Powrót sieci = natychmiastowa próba, bez czekania aż użytkownik kliknie.
    fromEvent(window, 'online')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.retry());
  }

  protected retry(): void {
    if (this.retrying()) {
      return;
    }
    this.retrying.set(true);

    this.auth.retryHydrate().subscribe((outcome) => {
      this.retrying.set(false);

      if (outcome.kind === 'unavailable') {
        return; // Nadal brak sieci — zostajemy na ekranie, `online` albo przycisk spróbują ponownie.
      }
      if (outcome.kind === 'anonymous') {
        void this.router.navigate(['/login'], { replaceUrl: true });
        return;
      }
      void this.router.navigateByUrl(this.returnUrl(), { replaceUrl: true });
    });
  }

  private returnUrl(): string {
    return (
      this.route.snapshot.queryParamMap.get(OFFLINE_RETURN_URL_PARAM) ??
      defaultAdminRouteForRole(this.auth.currentRole(), this.auth.currentEmployeeId())
    );
  }
}
