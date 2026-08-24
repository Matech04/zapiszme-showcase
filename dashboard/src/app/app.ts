import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Toast } from "primeng/toast";
import { ConfirmDialog } from "primeng/confirmdialog";
import { SalonTitleService } from '@core/services/salon-title.service';
import { CappedMessageService } from '@core/services/capped-message.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, Toast, ConfirmDialog],
  template: `
  <router-outlet></router-outlet>

    <!--
      Toasty u góry: na dole zasłaniały dolny pasek nawigacji (mobile) i przyciski akcji
      formularzy. onClose zwalnia slot limitu — PrimeNG emituje je zarówno po
      auto-wygaśnięciu, jak i po kliknięciu "x" (ToastItem.onAfterLeave).

      To JEDYNY p-toast w aplikacji: kolejny outlet bez atrybutu key renderowałby te same
      wiadomości po raz drugi i rozjechałby licznik w CappedMessageService.
    -->
    <p-toast
      position="top-right"
      styleClass="app-toast"
      (onClose)="toasts.release()"
    />

    <p-confirmDialog [style]="{ width: '25rem' }" />
  `
})
export class App {
  readonly _salonTitle = inject(SalonTitleService);
  protected readonly toasts = inject(CappedMessageService);
}
