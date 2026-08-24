import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { MessageService } from 'primeng/api';
import { Subject, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationsClient } from '@core/api/api-client';
import { PushNotificationsService } from './push-notifications.service';

function makeWindow(overrides: Record<string, unknown> = {}) {
  return {
    Notification: class {},
    PushManager: class {},
    navigator: { userAgent: 'Chrome', platform: 'Linux', maxTouchPoints: 0, standalone: false },
    matchMedia: () => ({ matches: false }),
    ...overrides,
  };
}

describe('PushNotificationsService', () => {
  let swPush: any;
  let api: { getVapidPublicKey: any; subscribePush: any; unsubscribePush: any };
  let messages: { add: ReturnType<typeof vi.fn> };
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };

  function build(win: Record<string, unknown>, isEnabled = true) {
    swPush = {
      isEnabled,
      subscription: new Subject(),
      notificationClicks: new Subject(),
      requestSubscription: vi.fn().mockResolvedValue({
        endpoint: 'https://push/ep',
        toJSON: () => ({ keys: { p256dh: 'PKEY', auth: 'AKEY' } }),
      }),
      unsubscribe: vi.fn().mockResolvedValue(true),
    };
    api = {
      getVapidPublicKey: vi.fn().mockReturnValue(of({ publicKey: 'VAPID', enabled: true })),
      subscribePush: vi.fn().mockReturnValue(of(null)),
      unsubscribePush: vi.fn().mockReturnValue(of(null)),
    };
    messages = { add: vi.fn() };
    router = { navigateByUrl: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        PushNotificationsService,
        { provide: SwPush, useValue: swPush },
        { provide: NotificationsClient, useValue: api },
        { provide: MessageService, useValue: messages },
        { provide: Router, useValue: router },
        { provide: DOCUMENT, useValue: { defaultView: win } },
      ],
    });
    return TestBed.inject(PushNotificationsService);
  }

  beforeEach(() => vi.clearAllMocks());

  it('subscribes and posts the subscription to the backend on enable', async () => {
    const service = build(makeWindow());
    await service.enable();

    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: 'VAPID' });
    expect(api.subscribePush).toHaveBeenCalledWith({
      endpoint: 'https://push/ep',
      p256dh: 'PKEY',
      auth: 'AKEY',
    });
    expect(service.subscribed()).toBe(true);
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }));
  });

  it('does not subscribe when push is unsupported (SW disabled)', async () => {
    const service = build(makeWindow(), false);
    await service.enable();

    expect(api.subscribePush).not.toHaveBeenCalled();
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn' }));
  });

  it('shows an install hint on iOS Safari (not standalone) instead of subscribing', async () => {
    const iosWin = makeWindow({
      navigator: { userAgent: 'iPhone', platform: 'iPhone', maxTouchPoints: 5, standalone: false },
    });
    const service = build(iosWin);
    await service.enable();

    expect(api.subscribePush).not.toHaveBeenCalled();
    expect(messages.add).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'Zainstaluj aplikację' }),
    );
  });

  it('unsubscribes on the backend and browser on disable', async () => {
    const service = build(makeWindow());
    swPush.subscription = of({ endpoint: 'https://push/ep' });
    await service.disable();

    expect(api.unsubscribePush).toHaveBeenCalledWith({ endpoint: 'https://push/ep' });
    expect(swPush.unsubscribe).toHaveBeenCalled();
    expect(service.subscribed()).toBe(false);
  });

  it('navigates to the deep-link when a notification is clicked', () => {
    const service = build(makeWindow());
    service.init();

    swPush.notificationClicks.next({
      notification: { data: { onActionClick: { default: { url: '/admin/schedule?appointment=x' } } } },
    });

    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin/schedule?appointment=x');
  });
});
