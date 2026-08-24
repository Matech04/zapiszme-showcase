import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { FormFieldComponent } from '@shared/ui/forms/form-field-component';
import { WizardShellComponent } from '../ui/wizard-shell.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/** Krok „Jak się nazywasz?" — buforuje imię i nazwisko. Zapis dopiero w kroku „salon". */
@Component({
  selector: 'app-onboarding-profile-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WizardShellComponent, FormFieldComponent, FormField],
  template: `
    <app-wizard-shell
      step="profile"
      title="Jak się nazywasz?"
      subtitle="Tak podpiszemy Twój profil pracownika w kalendarzu."
      [nextDisabled]="profileForm().invalid()"
      (next)="onNext()"
    >
      <div class="grid gap-5 sm:grid-cols-2">
        <app-form-field
          testId="setup-first-name"
          label="Imię"
          id="firstName"
          placeholder="np. Jan"
          [formField]="profileForm.firstName"
        />
        <app-form-field
          testId="setup-last-name"
          label="Nazwisko"
          id="lastName"
          placeholder="np. Kowalski"
          [formField]="profileForm.lastName"
        />
      </div>
    </app-wizard-shell>
  `,
})
export class OnboardingProfileStepComponent {
  private readonly store = inject(OnboardingWizardStore);
  private readonly router = inject(Router);

  protected readonly model = signal({
    firstName: this.store.firstName(),
    lastName: this.store.lastName(),
  });

  protected readonly profileForm = form(this.model, (path) => {
    required(path.firstName, { message: 'Imię jest wymagane' });
    maxLength(path.firstName, 100, { message: 'Maksymalnie 100 znaków' });
    required(path.lastName, { message: 'Nazwisko jest wymagane' });
    maxLength(path.lastName, 100, { message: 'Maksymalnie 100 znaków' });
  });

  protected onNext(): void {
    if (this.profileForm().invalid()) {
      return;
    }
    const value = this.profileForm().value();
    this.store.firstName.set(value.firstName.trim());
    this.store.lastName.set(value.lastName.trim());
    this.store.persistProfile();
    void this.router.navigate(['/setup/salon']);
  }
}
