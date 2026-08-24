import type { APIRequestContext } from '@playwright/test';

const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5199';

/**
 * Backdoor seed result returned by POST /api/_e2e/seed.
 * Source: backend/src/App.Api/Controllers/E2eController.cs#Seed
 */
export interface E2eSeedResult {
  tenantId: string;
  tenantSlug: string;
  employeeId: string;
  serviceId: string;
  serviceCategoryId: string;
  vatRateId: string;
  customerId: string;
  ownerUserId: string;
  ownerEmail: string;
}

/** Cienki wrapper na backdoor /api/_e2e/* — używa Playwright APIRequestContext (bez cookies). */
export class E2eApi {
  constructor(private readonly request: APIRequestContext) {}

  static apiUrl(path: string): string {
    return `${API_URL}${path}`;
  }

  async seed(): Promise<E2eSeedResult> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed'));
    if (!res.ok()) {
      throw new Error(`/api/_e2e/seed → HTTP ${res.status()} ${await res.text()}`);
    }
    return (await res.json()) as E2eSeedResult;
  }

  async advanceTime(seconds: number): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl(`/api/_e2e/time/advance?seconds=${seconds}`));
    if (!res.ok()) {
      throw new Error(`/api/_e2e/time/advance → HTTP ${res.status()}`);
    }
  }

  async resetTime(): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/time/reset'));
    if (!res.ok()) {
      throw new Error(`/api/_e2e/time/reset → HTTP ${res.status()}`);
    }
  }

  async getLastOtp(email: string): Promise<{ code: string; salonName: string } | null> {
    const res = await this.request.get(E2eApi.apiUrl(`/api/_e2e/otp/${encodeURIComponent(email)}`));
    if (res.status() === 404) return null;
    if (!res.ok()) {
      throw new Error(`/api/_e2e/otp → HTTP ${res.status()}`);
    }
    return (await res.json()) as { code: string; salonName: string };
  }

  async getLastAuthEmail(email: string): Promise<{
    lastConfirmEmailUrl: string | null;
    lastPasswordResetUrl: string | null;
    lastEmployeeInviteUrl: string | null;
  }> {
    const res = await this.request.get(
      E2eApi.apiUrl(`/api/_e2e/auth-email/${encodeURIComponent(email)}`),
    );
    if (!res.ok()) {
      throw new Error(`/api/_e2e/auth-email → HTTP ${res.status()}`);
    }
    return await res.json();
  }

  async runBgReminders(): Promise<void> {
    await this.request.post(E2eApi.apiUrl('/api/_e2e/bg/run-reminders'));
  }

  async runBgStatusLifecycle(): Promise<void> {
    await this.request.post(E2eApi.apiUrl('/api/_e2e/bg/run-status-lifecycle'));
  }

  async runBgCleanupUnconfirmed(): Promise<void> {
    await this.request.post(E2eApi.apiUrl('/api/_e2e/bg/run-cleanup-unconfirmed'));
  }

  // Sprint E extensions
  async seedUnconfirmedUser(email: string, ageHours = 49): Promise<{ userId: string; createdAt: string }> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-unconfirmed-user'), {
      data: { email, ageHours },
    });
    if (!res.ok()) throw new Error(`seed-unconfirmed-user → HTTP ${res.status()} ${await res.text()}`);
    return (await res.json()) as { userId: string; createdAt: string };
  }

  async seedEmployeeSchedule(employeeId: string, start = '09:00:00', end = '17:00:00'): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-employee-schedule'), {
      data: { employeeId, start, end },
    });
    if (!res.ok()) throw new Error(`seed-employee-schedule → HTTP ${res.status()}`);
  }

  /** Ustawia pracownika w trybie stałych slotów (FixedStartTimes) z podanymi godzinami pon-pt. */
  async seedEmployeeFixedSchedule(employeeId: string, startTimes = ['09:00:00', '12:00:00', '15:00:00']): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-employee-fixed-schedule'), {
      data: { employeeId, startTimes },
    });
    if (!res.ok()) throw new Error(`seed-employee-fixed-schedule → HTTP ${res.status()}`);
  }

  async seedEmployeeLeave(employeeId: string, startDate: string, endDate: string): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-employee-leave'), {
      data: { employeeId, startDate, endDate },
    });
    if (!res.ok()) throw new Error(`seed-employee-leave → HTTP ${res.status()}`);
  }

  async seedAppointment(opts: {
    employeeId: string;
    serviceId: string;
    customerId: string;
    date: string;
    startTime: string;
    status?: 'Pending' | 'AwaitingOtp' | 'Booked' | 'InProgress' | 'Completed' | 'Canceled';
    durationMinutes?: number;
  }): Promise<{ appointmentId: string; status: string }> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-appointment'), {
      data: { status: 'Booked', durationMinutes: 30, ...opts },
    });
    if (!res.ok()) throw new Error(`seed-appointment → HTTP ${res.status()} ${await res.text()}`);
    return (await res.json()) as { appointmentId: string; status: string };
  }

  async expireResetToken(email: string): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/expire-reset-token'), { data: { email } });
    if (!res.ok()) throw new Error(`expire-reset-token → HTTP ${res.status()}`);
  }

  async cleanup(): Promise<void> {
    await this.request.post(E2eApi.apiUrl('/api/_e2e/cleanup'));
  }

  /**
   * Ostatni kod SMS OTP wysłany dla podanego usera. Backdoor mapuje userId → numer
   * telefonu i ostatni przechwycony kod z TestPhoneOtpMailbox.
   */
  async getLastPhoneOtp(userId: string): Promise<{ phone: string; code: string } | null> {
    const res = await this.request.get(E2eApi.apiUrl(`/api/_e2e/phone-otp/${userId}`));
    if (res.status() === 404) return null;
    if (!res.ok()) {
      throw new Error(`/api/_e2e/phone-otp → HTTP ${res.status()}`);
    }
    return (await res.json()) as { phone: string; code: string };
  }

  // Sprint G' extensions
  async seedEmployeeWithCredentials(email: string, role = 'Employee'): Promise<{ userId: string; employeeId: string }> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-employee-with-credentials'), {
      data: { email, role },
    });
    if (!res.ok()) throw new Error(`seed-employee-with-credentials → HTTP ${res.status()} ${await res.text()}`);
    return (await res.json()) as { userId: string; employeeId: string };
  }

  async setCustomerWhitelist(customerId: string, isWhitelisted: boolean): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/set-customer-whitelist'), {
      data: { customerId, isWhitelisted },
    });
    if (!res.ok()) throw new Error(`set-customer-whitelist → HTTP ${res.status()}`);
  }

  async setTenantSettings(opts: {
    bookingAccessPolicy?: string;
    confirmationMode?: string;
    customerVerificationChannel?: string;
    staffCalendarVisibilityPolicy?: string;
    requireCustomerName?: boolean;
  }): Promise<void> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/set-tenant-settings'), { data: opts });
    if (!res.ok()) throw new Error(`set-tenant-settings → HTTP ${res.status()}`);
  }

  async seedNotification(message = 'Test notification', type = 'Generic'): Promise<{ notificationId: string | null }> {
    const res = await this.request.post(E2eApi.apiUrl('/api/_e2e/seed-notification'), {
      data: { message, type },
    });
    if (!res.ok()) {
      // Soft-fail — bez znanej sygnatury Notification reflection może nie zadziałać.
      return { notificationId: null };
    }
    return (await res.json()) as { notificationId: string | null };
  }
}
