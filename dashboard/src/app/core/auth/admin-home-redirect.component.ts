import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthSessionService } from './auth-session.service';
import { defaultAdminRouteForRole } from './default-admin-route';
import { OFFLINE_PATH } from './offline-route';

@Component({
  standalone: true,
  template: `<div class="p-6 text-center text-surface-400 text-sm">Ładowanie panelu…</div>`,
})
export class AdminHomeRedirectComponent implements OnInit {
  private readonly auth = inject(AuthSessionService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.auth.hydrate().subscribe((outcome) => {
      if (outcome.kind === 'unavailable') {
        void this.router.navigate([OFFLINE_PATH], { replaceUrl: true });
        return;
      }
      if (outcome.kind === 'anonymous') {
        void this.router.navigate(['/login'], { replaceUrl: true });
        return;
      }
      void this.router.navigate(
        [defaultAdminRouteForRole(this.auth.currentRole(), this.auth.currentEmployeeId())],
        {
          replaceUrl: true,
        },
      );
    });
  }
}
