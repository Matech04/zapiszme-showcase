import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { MessageService } from 'primeng/api';

@Component({
  standalone: true,
  template: `<div class="p-6 text-center text-surface-400 text-sm">Ładowanie dostępności…</div>`,
})
export class EmployeeMyAvailabilityRedirectComponent implements OnInit {
  private readonly auth = inject(AuthSessionService);
  private readonly router = inject(Router);
  private readonly messages = inject(MessageService);

  ngOnInit(): void {
    const role = this.auth.currentRole();
    const employeeId = this.auth.currentEmployeeId();

    if (employeeId) {
      void this.router.navigate(['/admin/my-availability', employeeId], { replaceUrl: true });
      return;
    }

    /** Owner/manager bez własnego profilu pracownika — kierujemy do listy zasobów/zespołu, gdzie mogą wybrać konkretnego pracownika. */
    if (role === 'owner' || role === 'manager') {
      this.messages.add({
        severity: 'info',
        summary: 'Brak profilu pracownika',
        detail: 'Twoje konto nie jest powiązane z profilem pracownika — wybierz pracownika, którego dostępność chcesz edytować.',
        life: 4500,
      });
      void this.router.navigate(['/admin/resources'], { replaceUrl: true });
      return;
    }

    this.messages.add({
      severity: 'warn',
      summary: 'Brak profilu',
      detail: 'Twoje konto nie jest powiązane z profilem pracownika.',
      life: 4000,
    });
    void this.router.navigate(['/admin/schedule'], { replaceUrl: true });
  }
}
