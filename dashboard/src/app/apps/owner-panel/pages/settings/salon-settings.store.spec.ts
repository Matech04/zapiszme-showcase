import { Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import {
  CustomerVerificationChannel,
  FileResponse,
  SalonSettingsClient,
  StaffCalendarVisibilityPolicy,
  TenantDto,
} from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { DashboardThemeService } from '@core/theme/dashboard-theme.service';
import { BookingPauseStore } from '@core/services/booking-pause.store';
import { SalonSettingsStore } from './salon-settings.store';

@Component({ standalone: true, template: '' })
class HostComponent {
  readonly store = inject(SalonSettingsStore);
}

describe('SalonSettingsStore', () => {
  let fixture: ComponentFixture<HostComponent>;
  let store: SalonSettingsStore;

  const tenantDto: TenantDto = {
    id: 't-1',
    name: 'Salon Testowy',
    slug: 'salon-testowy',
    customerVerificationChannel: CustomerVerificationChannel.Email,
    appointmentSlotStepMinutes: 30,
  };

  let salonClientMock: {
    get: ReturnType<typeof vi.fn>;
    put: ReturnType<typeof vi.fn>;
    getSlugAvailability: ReturnType<typeof vi.fn>;
    purgeAppointmentHistory: ReturnType<typeof vi.fn>;
    setBookingPause: ReturnType<typeof vi.fn>;
  };
  let messagesMock: { add: ReturnType<typeof vi.fn> };
  let themeMock: {
    pickerSeedHex: ReturnType<typeof vi.fn>;
    setAndPersistPrimaryHex: ReturnType<typeof vi.fn>;
    clearCustomPrimary: ReturnType<typeof vi.fn>;
  };
  let bookingPauseMock: { set: ReturnType<typeof vi.fn> };

  const okPutResponse: FileResponse = { data: new Blob(), status: 204 };

  /**
   * onSave guard'uje submit aż debounce'owany resolver slug-availability zakończy się sukcesem
   * (`slugCheckBlocksSubmit`). W teście pomijamy 450ms debounce, ustawiając prywatne signały ręcznie.
   */
  function markSlugAsAvailable(slug: string): void {
    const s = store as unknown as {
      slugAvailabilityCache: Map<string, boolean>;
      slugAsyncFor: { set: (v: string | null) => void };
      slugAsyncState: { set: (v: 'idle' | 'loading' | 'ok' | 'taken' | 'networkError') => void };
    };
    s.slugAvailabilityCache.set(slug, true);
    s.slugAsyncFor.set(slug);
    s.slugAsyncState.set('ok');
  }

  beforeEach(async () => {
    salonClientMock = {
      get: vi.fn().mockReturnValue(of(tenantDto)),
      put: vi.fn().mockReturnValue(of(okPutResponse)),
      // Debounce subscription `resolveSlugAvailabilityAfterDebounce` woła to po zmianie slug.
      getSlugAvailability: vi.fn().mockReturnValue(of({ available: true })),
      purgeAppointmentHistory: vi.fn().mockReturnValue(of({ deletedCount: 3 })),
      setBookingPause: vi.fn().mockReturnValue(of(okPutResponse)),
    };
    messagesMock = { add: vi.fn() };
    themeMock = {
      pickerSeedHex: vi.fn().mockReturnValue('#111827'),
      setAndPersistPrimaryHex: vi.fn().mockReturnValue(true),
      clearCustomPrimary: vi.fn(),
    };
    bookingPauseMock = { set: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        SalonSettingsStore,
        { provide: SalonSettingsClient, useValue: salonClientMock },
        { provide: MessageService, useValue: messagesMock },
        { provide: DashboardThemeService, useValue: themeMock },
        { provide: BookingPauseStore, useValue: bookingPauseMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    store = fixture.componentInstance.store;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('tworzy store', () => {
    expect(store).toBeTruthy();
  });

  it('wczytuje nazwę, slug i interwał slotów z API do modelu', () => {
    expect(store.salonModel().name).toBe('Salon Testowy');
    expect(store.salonModel().slug).toBe('salon-testowy');
    expect(store.salonModel().appointmentSlotStepMinutes).toBe('30');
  });

  it('purgeAppointmentHistory woła klienta i pokazuje toast z liczbą usuniętych', async () => {
    salonClientMock.purgeAppointmentHistory.mockReturnValue(of({ deletedCount: 3 }));

    await store.purgeAppointmentHistory();

    expect(salonClientMock.purgeAppointmentHistory).toHaveBeenCalledTimes(1);
    const toast = messagesMock.add.mock.calls.at(-1)?.[0];
    expect(toast?.severity).toBe('success');
    expect(toast?.detail).toContain('3');
    expect(store.purgeConfirming()).toBe(false);
  });

  it('przy zapisie wywołuje PUT z appointmentSlotStepMinutes', async () => {
    markSlugAsAvailable('salon-testowy');
    await store.onSave();

    expect(salonClientMock.put).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Salon Testowy',
        slug: 'salon-testowy',
        customerVerificationChannel: CustomerVerificationChannel.Email,
        appointmentSlotStepMinutes: 30,
      }),
    );
    expect(messagesMock.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', summary: 'Zapisano' }),
    );
  });

  it('przy błędzie PUT pokazuje komunikat o błędzie', async () => {
    markSlugAsAvailable('salon-testowy');
    salonClientMock.put.mockReturnValueOnce(throwError(() => new Error('network')));

    await store.onSave();

    expect(messagesMock.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'error', summary: 'Błąd zapisu' }),
    );
  });

  it('mapuje StaffCalendarVisibilityPolicy z DTO na model formularza', async () => {
    salonClientMock.get.mockReturnValue(
      of({ ...tenantDto, staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.TeamFull }),
    );
    store.settings.reload();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(store.salonModel().staffCalendarVisibilityPolicy).toBe('team_full');
  });

  it('domyślnie (brak pola w DTO) ustawia policy na "own"', () => {
    expect(store.salonModel().staffCalendarVisibilityPolicy).toBe('own');
  });

  it('settery pól „wyboru" aktualizują model', () => {
    store.setStaffCalendarVisibilityPolicy('team_read');
    expect(store.salonModel().staffCalendarVisibilityPolicy).toBe('team_read');
    store.setBookingAccessPolicy('invite_only');
    expect(store.salonModel().bookingAccessPolicy).toBe('invite_only');
    store.setRequireCustomerName(true);
    expect(store.salonModel().requireCustomerName).toBe(true);
  });

  it('toggleBookingPause zapisuje stan przez dedykowany endpoint i store pauzy', async () => {
    await store.toggleBookingPause(true);

    expect(salonClientMock.setBookingPause).toHaveBeenCalledWith(
      expect.objectContaining({ paused: true }),
    );
    expect(bookingPauseMock.set).toHaveBeenCalledWith(true);
    expect(store.bookingPaused()).toBe(true);
  });
});
