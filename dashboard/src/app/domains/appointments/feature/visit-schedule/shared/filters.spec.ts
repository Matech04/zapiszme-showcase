import { describe, expect, it } from 'vitest';
import { AppointmentPreviewDto, AppointmentStatus } from '@core/api/api-client';
import { filterAppointments, statusVariantFromPreview } from './filters';
import type { CalendarFiltersValue } from '../filters/calendar-filters.component';

function appt(
  partial: Partial<AppointmentPreviewDto> & { statusName?: string; statusId?: number },
): AppointmentPreviewDto {
  const { statusName, statusId, ...rest } = partial;
  const status: AppointmentStatus | undefined =
    statusName || statusId
      ? ({ id: statusId, name: statusName } as AppointmentStatus)
      : undefined;
  return { ...rest, status } as AppointmentPreviewDto;
}

const emptyFilters: CalendarFiltersValue = { employeeIds: [], statuses: [], searchQuery: '' };

describe('filterAppointments — statuses', () => {
  const pending = appt({ id: 'p', statusName: 'Pending' });
  const booked = appt({ id: 'b', statusName: 'Booked' });
  const canceled = appt({ id: 'c', statusName: 'Canceled' });
  const completed = appt({ id: 'd', statusName: 'Completed' });
  const list = [pending, booked, canceled, completed];

  it('domyślnie (statuses puste) pokazuje wszystkie aktywne, w tym pending; canceled ukryte', () => {
    const out = filterAppointments(list, { ...emptyFilters });
    expect(out.map((x) => x.id).sort()).toEqual(['b', 'd', 'p']);
  });

  it('zaznaczony pending pokazuje tylko pending', () => {
    const out = filterAppointments(list, { ...emptyFilters, statuses: ['pending'] });
    expect(out.map((x) => x.id)).toEqual(['p']);
  });

  it('zaznaczony canceled pokazuje TYLKO canceled (ukrywa inne)', () => {
    const out = filterAppointments(list, { ...emptyFilters, statuses: ['canceled'] });
    expect(out.map((x) => x.id)).toEqual(['c']);
  });

  it('zaznaczone booked+canceled pokazuje oba', () => {
    const out = filterAppointments(list, { ...emptyFilters, statuses: ['booked', 'canceled'] });
    expect(out.map((x) => x.id).sort()).toEqual(['b', 'c']);
  });
});

describe('filterAppointments — pracownicy', () => {
  it('puste employeeIds przepuszczają każdą wizytę', () => {
    const a = appt({ id: 'a', employeeId: 'e1', statusName: 'Booked' });
    const b = appt({ id: 'b', employeeId: 'e2', statusName: 'Booked' });
    expect(filterAppointments([a, b], { ...emptyFilters }).map((x) => x.id).sort()).toEqual([
      'a',
      'b',
    ]);
  });

  it('niepuste employeeIds zawężają do wybranych', () => {
    const a = appt({ id: 'a', employeeId: 'e1', statusName: 'Booked' });
    const b = appt({ id: 'b', employeeId: 'e2', statusName: 'Booked' });
    const out = filterAppointments([a, b], { ...emptyFilters, employeeIds: ['e1'] });
    expect(out.map((x) => x.id)).toEqual(['a']);
  });
});

describe('filterAppointments — fraza', () => {
  const list = [
    appt({ id: '1', customerFirstName: 'Anna', customerLastName: 'Kowalska', statusName: 'Booked' }),
    appt({ id: '2', customerPhoneNumber: '+48 500 600 700', statusName: 'Booked' }),
    appt({ id: '3', serviceName: 'Strzyżenie męskie', statusName: 'Booked' }),
  ];

  it('matchuje po imieniu', () => {
    expect(filterAppointments(list, { ...emptyFilters, searchQuery: 'anna' }).map((x) => x.id))
      .toEqual(['1']);
  });

  it('matchuje po fragmencie numeru telefonu', () => {
    expect(filterAppointments(list, { ...emptyFilters, searchQuery: '500 600' }).map((x) => x.id))
      .toEqual(['2']);
  });

  it('matchuje po nazwie usługi (case-insensitive)', () => {
    expect(
      filterAppointments(list, { ...emptyFilters, searchQuery: 'STRZYŻ' }).map((x) => x.id),
    ).toEqual(['3']);
  });

  it('puste wyniki gdy nic nie matchuje', () => {
    expect(filterAppointments(list, { ...emptyFilters, searchQuery: 'xyz' })).toEqual([]);
  });
});

describe('filterAppointments — krawędzie', () => {
  it('null/undefined → []', () => {
    expect(filterAppointments(undefined, { ...emptyFilters })).toEqual([]);
    expect(filterAppointments(null, { ...emptyFilters })).toEqual([]);
    expect(filterAppointments([], { ...emptyFilters })).toEqual([]);
  });

  it('whitespace w searchQuery jest trymowany', () => {
    const a = appt({ id: 'a', customerFirstName: 'Ola', statusName: 'Booked' });
    expect(filterAppointments([a], { ...emptyFilters, searchQuery: '   ' }).map((x) => x.id))
      .toEqual(['a']);
  });
});

describe('statusVariantFromPreview', () => {
  it('PascalCase Status działa tak samo jak camelCase status', () => {
    const camel = appt({ statusName: 'Pending' });
    const pascal = { Status: { Name: 'Pending' } } as unknown as AppointmentPreviewDto;
    expect(statusVariantFromPreview(camel)).toBe('pending');
    expect(statusVariantFromPreview(pascal)).toBe('pending');
  });
});
