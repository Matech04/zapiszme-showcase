import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { MessageService } from 'primeng/api';
import {
  AppointmentPreviewDto,
  AppointmentsClient,
  SwapPreviewDto,
} from '@core/api/api-client';
import { SwapAppointmentsDialogComponent } from './swap-appointments-dialog.component';

describe('SwapAppointmentsDialogComponent', () => {
  let fixture: ComponentFixture<SwapAppointmentsDialogComponent>;
  let component: SwapAppointmentsDialogComponent;

  let appointmentsClientMock: {
    previewSwap: ReturnType<typeof vi.fn>;
    swapAppointments: ReturnType<typeof vi.fn>;
  };
  let messagesMock: { add: ReturnType<typeof vi.fn> };

  const first = { id: 'a1', serviceName: 'Koloryzacja', startTime: '10:00:00', endTime: '11:00:00', date: '2026-09-20' as unknown as Date } as AppointmentPreviewDto;
  const second = { id: 'a2', serviceName: 'Strzyżenie', startTime: '13:00:00', endTime: '13:30:00', date: '2026-09-20' as unknown as Date } as AppointmentPreviewDto;

  function preview(p: Partial<SwapPreviewDto>): SwapPreviewDto {
    return {
      equalDuration: false,
      plainSwapFits: false,
      harmonizationAvailable: false,
      harmonizedSwapFits: false,
      ...p,
    } as SwapPreviewDto;
  }

  beforeEach(async () => {
    appointmentsClientMock = {
      previewSwap: vi.fn().mockReturnValue(of(preview({ equalDuration: true, plainSwapFits: true }))),
      swapAppointments: vi.fn().mockReturnValue(of({ firstAppointmentId: 'a1', secondAppointmentId: 'a2' })),
    };
    messagesMock = { add: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [SwapAppointmentsDialogComponent],
      providers: [
        { provide: AppointmentsClient, useValue: appointmentsClientMock },
        { provide: MessageService, useValue: messagesMock },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(SwapAppointmentsDialogComponent);
    component = fixture.componentInstance;
  });

  function open(): void {
    fixture.componentRef.setInput('first', first);
    fixture.componentRef.setInput('second', second);
    fixture.detectChanges();
  }

  it('isVisible false dopóki brak obu wizyt', () => {
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(false);
    fixture.componentRef.setInput('first', first);
    fixture.detectChanges();
    expect((component as unknown as { isVisible: () => boolean }).isVisible()).toBe(false);
  });

  it('po otwarciu pobiera podgląd dla obu wizyt', async () => {
    open();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(appointmentsClientMock.previewSwap).toHaveBeenCalledWith('a1', 'a2');
  });

  it('plainSwapFits → canPlainSwap i submit bez harmonizacji', async () => {
    open();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['canPlainSwap']()).toBe(true);
    expect(component['canHarmonize']()).toBe(false);

    let emitted = false;
    component.success.subscribe(() => (emitted = true));
    component['onSubmit']();

    expect(appointmentsClientMock.swapAppointments).toHaveBeenCalledWith(
      expect.objectContaining({ firstAppointmentId: 'a1', secondAppointmentId: 'a2', harmonizeToShorter: false }),
    );
    expect(emitted).toBe(true);
    expect(messagesMock.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }));
  });

  it('plain nie mieści się, ale harmonizacja możliwa → submit z harmonizeToShorter=true', async () => {
    appointmentsClientMock.previewSwap.mockReturnValue(
      of(preview({ plainSwapFits: false, harmonizationAvailable: true, harmonizedSwapFits: true, serviceChangeAppointmentId: 'a1', fromServiceName: 'Koloryzacja', toServiceName: 'Strzyżenie', oldPrice: 120, newPrice: 50 })),
    );
    open();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component['canPlainSwap']()).toBe(false);
    expect(component['canHarmonize']()).toBe(true);

    component['onSubmit']();
    expect(appointmentsClientMock.swapAppointments).toHaveBeenCalledWith(
      expect.objectContaining({ harmonizeToShorter: true }),
    );
  });

  it('nic nie pasuje → canSubmit false, brak wywołania swap', async () => {
    appointmentsClientMock.previewSwap.mockReturnValue(
      of(preview({ plainSwapFits: false, harmonizationAvailable: false, harmonizedSwapFits: false })),
    );
    open();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component['canSubmit']()).toBe(false);
    component['onSubmit']();
    expect(appointmentsClientMock.swapAppointments).not.toHaveBeenCalled();
  });

  it('błąd 409 ustawia komunikat', async () => {
    appointmentsClientMock.swapAppointments.mockReturnValue(throwError(() => ({ status: 409 })));
    open();
    await fixture.whenStable();
    fixture.detectChanges();
    component['onSubmit']();
    expect(component['submitError']()).toContain('zajęty');
  });
});
