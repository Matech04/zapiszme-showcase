import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { nextStepToRoute } from '@core/auth/setup.guard';

/**
 * Trasa indeksowa `/setup` — nie ma własnego widoku. Odczytuje stan onboardingu i przekierowuje
 * na właściwy pod-krok (`nextStep`), żeby refresh/wznowienie na innym urządzeniu trafiło tam,
 * gdzie właściciel skończył.
 */
@Component({
  selector: 'app-setup-index-redirect',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="p-8 text-center text-surface-400 text-sm">Wczytywanie kreatora…</div>`,
})
export class SetupIndexRedirectComponent implements OnInit {
  private readonly onboarding = inject(OnboardingStateService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.onboarding.ensure().subscribe((state) => {
      void this.router.navigate([nextStepToRoute(state?.nextStep)], { replaceUrl: true });
    });
  }
}
