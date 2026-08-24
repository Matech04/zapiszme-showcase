// app.routes.ts
import { Routes } from '@angular/router';
import { HomeRedirectComponent } from '@core/auth/home-redirect.component';
import { redirectWhenAuthenticatedGuard } from '@core/auth/redirect-when-authenticated.guard';
import { MainLayoutComponent } from '@layout/main-layout.component';
import { staffManagementGuard } from '@core/auth/staff-management.guard';
import { ownerOnlyGuard } from '@core/auth/owner-only.guard';
import { generalAccessGuard } from '@core/auth/general-access.guard';
import { SalonSettingsStore } from '@apps/owner-panel/pages/settings/salon-settings.store';
import { teamViewGuard } from '@core/auth/team-view.guard';
import { authGuard } from '@core/auth/auth.guard';
import { onboardingGuard } from '@core/auth/onboarding.guard';
import { setupGuard } from '@core/auth/setup.guard';
import { OnboardingWizardStore } from '@features/onboarding/onboarding-wizard.store';
import { AdminHomeRedirectComponent } from '@core/auth/admin-home-redirect.component';
import { dirtyFormGuard } from '@core/guards/dirty-form.guard';
import { systemAdminGuard } from '@core/auth/system-admin.guard';
import { salonContextGuard } from '@core/auth/salon-context.guard';
import { appointmentsUnblockedGuard } from '@core/auth/appointments-unblocked.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: HomeRedirectComponent },

  {
    path: 'login',
    canActivate: [redirectWhenAuthenticatedGuard],
    loadComponent: () =>
      import('@features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    canActivate: [redirectWhenAuthenticatedGuard],
    loadComponent: () =>
      import('@features/auth/register-owner.component').then((m) => m.RegisterOwnerComponent),
  },
  {
    path: 'confirm-email',
    loadComponent: () =>
      import('@features/auth/confirm-email.component').then((m) => m.ConfirmEmailComponent),
  },
  {
    path: 'confirm-phone',
    loadComponent: () =>
      import('@features/auth/confirm-phone.component').then((m) => m.ConfirmPhoneComponent),
  },
  {
    path: 'confirm-change-email',
    loadComponent: () =>
      import('@features/auth/confirm-change-email.component').then(
        (m) => m.ConfirmChangeEmailComponent,
      ),
  },
  {
    path: 'check-email',
    loadComponent: () =>
      import('@features/auth/check-email.component').then((m) => m.CheckEmailComponent),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('@features/auth/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('@features/auth/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  {
    path: 'accept-invite',
    loadComponent: () =>
      import('@features/auth/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  {
    path: 'login-failed',
    loadComponent: () =>
      import('@features/auth/login-failed.component').then((m) => m.LoginFailedComponent),
  },
  {
    // Sonda sesji nie doleciała do API. Osobny ekran zamiast `/login`, bo użytkownik NIE jest wylogowany.
    path: 'offline',
    loadComponent: () => import('@features/auth/offline.component').then((m) => m.OfflineComponent),
  },

  {
    // Kreator onboardingu. Store buforuje niezatwierdzone kroki (jedna instancja na całą sesję /setup).
    path: 'setup',
    canActivate: [setupGuard],
    providers: [OnboardingWizardStore],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('@features/onboarding/setup-index-redirect.component').then(
            (m) => m.SetupIndexRedirectComponent,
          ),
      },
      {
        path: 'profile',
        loadComponent: () =>
          import('@features/onboarding/steps/profile-step.component').then(
            (m) => m.OnboardingProfileStepComponent,
          ),
      },
      {
        // Nie krok kreatora — konto po dezaktywacji pracownika. Siedzi pod /setup, bo to tam
        // odbija je `onboardingGuard`, a `setupGuard` przepuszcza każdy nieukończony onboarding.
        path: 'konto-nieaktywne',
        loadComponent: () =>
          import('@features/onboarding/inactive-account.component').then(
            (m) => m.InactiveAccountComponent,
          ),
      },
      {
        path: 'salon',
        loadComponent: () =>
          import('@features/onboarding/steps/salon-step.component').then(
            (m) => m.OnboardingSalonStepComponent,
          ),
      },
      {
        path: 'industry',
        loadComponent: () =>
          import('@features/onboarding/steps/industry-step.component').then(
            (m) => m.OnboardingIndustryStepComponent,
          ),
      },
      {
        path: 'services',
        loadComponent: () =>
          import('@features/onboarding/steps/services-step.component').then(
            (m) => m.OnboardingServicesStepComponent,
          ),
      },
      {
        path: 'slot-mode',
        loadComponent: () =>
          import('@features/onboarding/steps/slot-mode-step.component').then(
            (m) => m.OnboardingSlotModeStepComponent,
          ),
      },
      {
        path: 'schedule',
        loadComponent: () =>
          import('@features/onboarding/steps/schedule-step.component').then(
            (m) => m.OnboardingScheduleStepComponent,
          ),
      },
      {
        path: 'rules',
        loadComponent: () =>
          import('@features/onboarding/steps/rules-step.component').then(
            (m) => m.OnboardingRulesStepComponent,
          ),
      },
      {
        path: 'done',
        loadComponent: () =>
          import('@features/onboarding/steps/done-step.component').then(
            (m) => m.OnboardingDoneStepComponent,
          ),
      },
    ],
  },

  {
    path: 'admin',
    component: MainLayoutComponent,
    canActivate: [authGuard, onboardingGuard],
    children: [
      { path: '', component: AdminHomeRedirectComponent, pathMatch: 'full' },
      {
        path: 'feedback',
        loadComponent: () =>
          import('@apps/owner-panel/pages/feedback-page.component').then(
            (c) => c.FeedbackPageComponent,
          ),
      },
      {
        // Katalog przewodników. Bez guarda roli — pracownik i recepcja też mają swoje
        // przewodniki; sama strona filtruje zawartość po roli.
        path: 'guides',
        loadComponent: () =>
          import('@apps/owner-panel/pages/guides-page.component').then(
            (c) => c.GuidesPageComponent,
          ),
      },
      {
        // Self-service konta (nazwa / e-mail / hasło) — dostępne dla każdej zalogowanej roli.
        path: 'account',
        loadComponent: () =>
          import('@apps/owner-panel/pages/account-page.component').then(
            (c) => c.AccountPageComponent,
          ),
      },
      // Dashboard usunięty — kalendarz jest stroną domową owner/managera.
      // Redirect zachowuje stare zakładki/bookmarki do /admin/dashboard.
      {
        path: 'dashboard',
        redirectTo: 'schedule',
        pathMatch: 'full',
      },
      {
        path: 'team',
        canActivate: [teamViewGuard],
        loadComponent: () =>
          import('@apps/owner-panel/pages/team-page.component').then((c) => c.TeamPageComponent),
      },
      {
        path: 'services',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@apps/owner-panel/pages/services-page.component').then(
            (c) => c.ServicesPageComponent,
          ),
      },
      {
        path: 'settings',
        canActivate: [staffManagementGuard],
        // Store współdzielony przez pod-strony Ustawień salonu (jedna instancja na sekcję, bo zapis
        // to jeden pełny PUT TenantDto). Instancjonowany leniwie — pod-strony bez niego (usage/vat)
        // go nie tworzą.
        providers: [SalonSettingsStore],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings-page.component').then(
                (c) => c.SettingsPageComponent,
              ),
          },
          {
            path: 'salon',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings/salon-brand-page.component').then(
                (c) => c.SalonBrandPageComponent,
              ),
          },
          {
            path: 'booking',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings/booking-rules-page.component').then(
                (c) => c.BookingRulesPageComponent,
              ),
          },
          {
            path: 'public-form',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings/public-form-page.component').then(
                (c) => c.PublicFormPageComponent,
              ),
          },
          {
            path: 'appearance',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings/appearance-page.component').then(
                (c) => c.AppearancePageComponent,
              ),
          },
          {
            path: 'reception',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/reception-page.component').then(
                (c) => c.ReceptionPageComponent,
              ),
          },
          {
            path: 'privacy',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/settings/privacy-page.component').then(
                (c) => c.PrivacyPageComponent,
              ),
          },
          {
            path: 'notifications',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/notification-settings-page.component').then(
                (c) => c.NotificationSettingsPageComponent,
              ),
          },
          {
            path: 'sms',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/sms-templates-page.component').then(
                (c) => c.SmsTemplatesPageComponent,
              ),
          },
          {
            path: 'usage',
            loadComponent: () =>
              import('@apps/owner-panel/pages/notification-usage-page.component').then(
                (c) => c.NotificationUsagePageComponent,
              ),
          },
          {
            path: 'deposits',
            canActivate: [ownerOnlyGuard],
            loadComponent: () =>
              import('@apps/owner-panel/pages/deposits-settings-page.component').then(
                (c) => c.DepositsSettingsPageComponent,
              ),
          },
          {
            path: 'vat',
            loadComponent: () =>
              import('@apps/owner-panel/pages/vat-rates-page.component').then(
                (c) => c.VatRatesPageComponent,
              ),
          },
        ],
      },
      {
        path: 'schedule/new',
        canActivate: [appointmentsUnblockedGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/appointments/feature/appointment-form/appointment-form.component').then(
            (c) => c.AppointmentFormComponent,
          ),
      },
      {
        path: 'appointment/:appointmentId/edit',
        canActivate: [appointmentsUnblockedGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/appointments/feature/appointment-form/appointment-form.component').then(
            (c) => c.AppointmentFormComponent,
          ),
      },
      {
        path: 'schedule/:employeeId',
        canActivate: [salonContextGuard],
        loadComponent: () =>
          import('@domains/appointments/feature/visit-schedule/visit-schedule.component').then(
            (c) => c.VisitScheduleComponent,
          ),
      },
      {
        path: 'schedule',
        canActivate: [salonContextGuard],
        loadComponent: () =>
          import('@domains/appointments/feature/visit-schedule/visit-schedule.component').then(
            (c) => c.VisitScheduleComponent,
          ),
      },
      {
        path: 'my-services',
        loadComponent: () =>
          import('@domains/employees/feature/my-services/employee-my-services-page.component').then(
            (c) => c.EmployeeMyServicesPageComponent,
          ),
      },
      {
        path: 'my-availability',
        loadComponent: () =>
          import('@domains/employees/feature/my-availability/employee-my-availability-redirect.component').then(
            (c) => c.EmployeeMyAvailabilityRedirectComponent,
          ),
      },
      {
        path: 'my-availability/:id',
        loadComponent: () =>
          import('@domains/employees/feature/availability/employee-availability-dashboard.component').then(
            (c) => c.EmployeeAvailabilityDashboardComponent,
          ),
      },
      {
        path: 'my-availability/:id/weekly-schedule',
        redirectTo: 'my-availability/:id/schedules',
        pathMatch: 'full',
      },
      {
        path: 'my-availability/:id/schedules',
        loadComponent: () =>
          import('@domains/employees/feature/availability/schedules-list/employee-schedules-list.component').then(
            (c) => c.EmployeeSchedulesListComponent,
          ),
      },
      {
        // Podgląd miesiąca wchłonięty przez zunifikowany kalendarz wizyt (widok miesiąca).
        path: 'my-availability/:id/full-schedule',
        redirectTo: (r) => `/admin/schedule/${r.params['id']}?view=month`,
      },
      {
        path: 'my-availability/:id/schedules/new',
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/weekly-schedule/weekly-schedule.component').then(
            (c) => c.WeeklyScheduleComponent,
          ),
      },
      {
        path: 'my-availability/:id/schedules/:scheduleId/edit',
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/weekly-schedule/weekly-schedule.component').then(
            (c) => c.WeeklyScheduleComponent,
          ),
      },
      {
        path: 'my-availability/:id/special-days',
        loadComponent: () =>
          import('@domains/employees/feature/availability/employee-special-days.component').then(
            (c) => c.EmployeeSpecialDaysComponent,
          ),
      },
      {
        path: 'my-availability/:id/leaves/new',
        loadComponent: () =>
          import('@domains/employees/feature/availability/leave-dashboard/employee-leave-form.component').then(
            (c) => c.EmployeeLeaveForm,
          ),
      },
      {
        path: 'my-availability/:id/leave-dashboard',
        loadComponent: () =>
          import('@domains/employees/feature/availability/leave-dashboard/employee-leave-dashboard.component').then(
            (c) => c.EmployeeLeaveDashboardComponent,
          ),
      },
      // Utrwalone/zewnętrzne linki do dawnych osobnych tras — przekierowanie do huba Ustawień.
      { path: 'salon', redirectTo: 'settings/salon', pathMatch: 'full' },
      { path: 'vat-rates', redirectTo: 'settings/vat', pathMatch: 'full' },
      {
        path: 'customers',
        canActivate: [generalAccessGuard],
        loadComponent: () =>
          import('@apps/owner-panel/pages/customers-page.component').then((c) => c.CustomersPageComponent),
      },
      {
        path: 'customers/new',
        canActivate: [generalAccessGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/customers/feature/customer-form/customer-form.component').then(
            (c) => c.CustomerFormComponent,
          ),
      },
      {
        path: 'customers/edit/:id',
        canActivate: [generalAccessGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/customers/feature/customer-form/customer-form.component').then(
            (c) => c.CustomerFormComponent,
          ),
      },
      {
        path: 'customers/:id',
        canActivate: [generalAccessGuard],
        loadComponent: () =>
          import('@domains/customers/feature/customer-profile/customer-profile.component').then(
            (c) => c.CustomerProfileComponent,
          ),
      },
      {
        path: 'resources',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@apps/owner-panel/pages/resources.component').then((c) => c.ResourcesPageComponent),
      },

      {
        path: 'resources/shift-templates',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/shiftTemplates/feature/shift-templates-list.component').then(
            (c) => c.ShiftTemplatesListComponent,
          ),
      },
      {
        path: 'resources/shift-templates/new',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/shiftTemplates/feature/shift-template-form.component').then(
            (c) => c.ShiftTemplateFormComponent,
          ),
      },
      {
        path: 'resources/shift-templates/:id/edit',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/shiftTemplates/feature/shift-template-form.component').then(
            (c) => c.ShiftTemplateFormComponent,
          ),
      },

      {
        path: 'resources/categories/new',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/services/feature/service-category-form.component').then((c) => c.ServiceCategoryForm),
      },
      {
        path: 'resources/categories/edit/:id',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/services/feature/service-category-form.component').then((c) => c.ServiceCategoryForm),
      },

      // Formularz usługi otwiera się jako drawer w `service-catalog`.
      // Stare deep-linki przekierowujemy do katalogu (zachowanie pamięci linków).
      {
        path: 'resources/service/new',
        redirectTo: 'services',
        pathMatch: 'full',
      },
      {
        path: 'resources/service/new/:categoryId',
        redirectTo: 'services',
        pathMatch: 'full',
      },
      {
        path: 'resources/service/edit/:id',
        redirectTo: 'services',
        pathMatch: 'full',
      },

      {
        path: 'resources/employees/new',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/employee-form/employee-form.component').then((c) => c.EmployeeForm),
      },
      {
        path: 'resources/employees/edit/:id',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/employee-form/employee-form.component').then((c) => c.EmployeeForm),
      },

      {
        path: 'resources/employees/:id/availability',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/employee-availability-dashboard.component').then(
            (c) => c.EmployeeAvailabilityDashboardComponent,
          ),
      },
      {
        path: 'resources/employees/:id/services',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/employee-services-dashboard/employee-services-dashboard.component').then(
            (c) => c.EmployeeServicesDashboardComponent,
          ),
      },
      {
        path: 'resources/employees/:id/weekly-schedule',
        redirectTo: 'resources/employees/:id/schedules',
        pathMatch: 'full',
      },
      {
        path: 'resources/employees/:id/schedules',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/schedules-list/employee-schedules-list.component').then(
            (c) => c.EmployeeSchedulesListComponent,
          ),
      },
      {
        // Podgląd miesiąca wchłonięty przez zunifikowany kalendarz wizyt (widok miesiąca).
        // Dostęp do cudzej dostępności egzekwuje samo /admin/schedule (scoping pracownika + backend).
        path: 'resources/employees/:id/full-schedule',
        redirectTo: (r) => `/admin/schedule/${r.params['id']}?view=month`,
      },
      {
        path: 'resources/employees/:id/schedules/new',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/weekly-schedule/weekly-schedule.component').then(
            (c) => c.WeeklyScheduleComponent,
          ),
      },
      {
        path: 'resources/employees/:id/schedules/:scheduleId/edit',
        canActivate: [staffManagementGuard],
        canDeactivate: [dirtyFormGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/weekly-schedule/weekly-schedule.component').then(
            (c) => c.WeeklyScheduleComponent,
          ),
      },
      {
        path: 'resources/employees/:id/special-days',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/employee-special-days.component').then(
            (c) => c.EmployeeSpecialDaysComponent,
          ),
      },
      {
        path: 'resources/employees/:id/leaves/new',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/leave-dashboard/employee-leave-form.component').then(
            (c) => c.EmployeeLeaveForm,
          ),
      },
      {
        path: 'resources/employees/:id/leave-dashboard',
        canActivate: [staffManagementGuard],
        loadComponent: () =>
          import('@domains/employees/feature/availability/leave-dashboard/employee-leave-dashboard.component').then(
            (c) => c.EmployeeLeaveDashboardComponent,
          ),
      },

      // System admin
      {
        path: 'system/tenants',
        canActivate: [systemAdminGuard],
        loadComponent: () =>
          import('@apps/system-admin/tenants-page.component').then(
            (c) => c.TenantsPageComponent,
          ),
      },
      {
        path: 'system/promocodes',
        canActivate: [systemAdminGuard],
        loadComponent: () =>
          import('@apps/system-admin/promocodes-page.component').then(
            (c) => c.PromoCodesPageComponent,
          ),
      },
      {
        path: 'system/sms-templates',
        canActivate: [systemAdminGuard],
        loadComponent: () =>
          import('@apps/system-admin/sms-templates-moderation-page.component').then(
            (c) => c.SmsTemplatesModerationPageComponent,
          ),
      },
      {
        path: 'system/maintenance',
        canActivate: [systemAdminGuard],
        loadComponent: () =>
          import('@apps/system-admin/maintenance-page.component').then(
            (c) => c.MaintenancePageComponent,
          ),
      },
    ],
  },

  { path: '**', redirectTo: '', pathMatch: 'full' },
];
