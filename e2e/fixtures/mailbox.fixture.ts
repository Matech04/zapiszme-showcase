import { test as base, expect } from '@playwright/test';
import { E2eApi } from '../helpers/api-client';

/**
 * Fixture wystawiająca operacje na in-memory mailboxach backendu (TestEmailTransport,
 * TestAuthEmailMailbox, TestBookingOtpMailbox — aktywne w env E2E).
 *
 * Polling z timeout — pozwala czekać aż e-mail/OTP dotrze (asynchroniczna wysyłka).
 */
export const test = base.extend<{
  mailbox: Mailbox;
}>({
  mailbox: async ({ request }, use) => {
    const api = new E2eApi(request);
    await use(new Mailbox(api));
  },
});

export class Mailbox {
  constructor(private readonly api: E2eApi) {}

  /** Czeka aż OTP dotrze dla danego e-maila. Default timeout 5s, polling co 100ms. */
  async waitForOtp(email: string, timeoutMs = 5_000): Promise<string> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const otp = await this.api.getLastOtp(email);
      if (otp) return otp.code;
      await sleep(100);
    }
    throw new Error(`Timed out waiting ${timeoutMs}ms for OTP to ${email}`);
  }

  /** Czeka na confirm-email URL po rejestracji. */
  async waitForConfirmEmailUrl(email: string, timeoutMs = 5_000): Promise<string> {
    return this.pollAuthUrl(email, 'lastConfirmEmailUrl', timeoutMs);
  }

  /** Czeka na password-reset URL. */
  async waitForPasswordResetUrl(email: string, timeoutMs = 5_000): Promise<string> {
    return this.pollAuthUrl(email, 'lastPasswordResetUrl', timeoutMs);
  }

  private async pollAuthUrl(
    email: string,
    field: 'lastConfirmEmailUrl' | 'lastPasswordResetUrl',
    timeoutMs: number,
  ): Promise<string> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const data = await this.api.getLastAuthEmail(email);
      const url = data[field];
      if (url) return url;
      await sleep(100);
    }
    throw new Error(`Timed out waiting ${timeoutMs}ms for ${field} (${email})`);
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

export { expect };
