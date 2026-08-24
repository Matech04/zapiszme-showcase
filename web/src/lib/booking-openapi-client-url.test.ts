import { describe, expect, it, vi } from 'vitest';
import { BookingApiClient } from './booking-openapi-client';

describe('BookingApiClient — budowa URL (available-slots)', () => {
  it('wstawia slug i opcjonalne query', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const client = new BookingApiClient('https://host', { fetch: fetchMock });

    await client.bookingAppointments_GetAvailableSlots(
      'mój-salon',
      '2026-06-15',
      'emp-9',
      ['svc-2'],
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const url = fetchMock.mock.calls[0][0] as string;
    expect(url).toBe(
      'https://host/api/booking/m%C3%B3j-salon/appointments/available-slots?date=2026-06-15&employeeId=emp-9&serviceIds=svc-2',
    );
  });
});
