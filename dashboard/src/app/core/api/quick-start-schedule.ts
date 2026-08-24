import { EmployeeScheduleDto, TimeRangeDto } from './api-client';

/** Sentinel używany do oznaczenia grafiku „bezterminowo / do odwołania" (zgodny z weekly-schedule.component). */
const INDEFINITE_ACTIVE_TO = '9999-12-31';

function todayYyyyMmDd(): string {
  const d = new Date();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

/**
 * Quick-start dla SOLO ownera: standardowy tydzień 9-17, poniedziałek-piątek,
 * jeden cykl, obowiązuje od dziś bezterminowo. Backend oczekuje stringów dat
 * w polach `Date` — dlatego rzutowanie przez `unknown` (taki sam wzorzec jak
 * w weekly-schedule.component.ts).
 *
 * cycleIndex: Sunday=0, Monday=1, ..., Saturday=6 (zgodne z DAY_OF_WEEK_INDEX).
 */
export function buildStandardWeekScheduleDto(): EmployeeScheduleDto {
  const workRange: TimeRangeDto = { startTime: '09:00:00', endTime: '17:00:00' };
  const mondayToFridayCycleIndexes = [1, 2, 3, 4, 5];

  return {
    activeFrom: todayYyyyMmDd() as unknown as Date,
    activeTo: INDEFINITE_ACTIVE_TO as unknown as Date,
    numberOfCycles: 1,
    days: mondayToFridayCycleIndexes.map((cycleIndex) => ({
      cycleIndex,
      workRanges: [workRange],
      breaks: [],
    })),
  };
}
