import { ParamMap, Params } from '@angular/router';

export type ReportGranularity = 'week' | 'month';

export type ReportPreset = 'week' | 'month' | 'last30';

/** docs/SPEC.md §5.1.1: an interval of at most 366 days, counted as the API counts it, from `from` to `to`. */
export const MAX_REPORT_DAYS = 366;

const DAY_MS = 24 * 60 * 60 * 1000;
const DAY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * The report page's filters (docs/UI_UX_SPEC.md §7.8), as the address holds them: `from` and `to` are days
 * where the person is, both included, `project` and `user` ids or null for every one.
 */
export interface ReportFilters {
  from: string;
  to: string;
  project: string | null;
  user: string | null;
  granularity: ReportGranularity;
}

/** The filters read from the address, and whether its interval was right: a wrong one gives the last 30 days. */
export interface ReadFilters {
  filters: ReportFilters;
  valid: boolean;
}

/** A day where the person is, as YYYY-MM-DD. */
export function dayOf(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${String(date.getFullYear())}-${month}-${day}`;
}

/** Midnight of that day where the person is, or null for a day that doesn't exist. */
export function dateOf(day: string): Date | null {
  const parts = DAY_PATTERN.exec(day);
  if (!parts) {
    return null;
  }
  const [year, month, date] = [Number(parts[1]), Number(parts[2]) - 1, Number(parts[3])];
  const midnight = new Date(year, month, date);
  return midnight.getFullYear() === year &&
    midnight.getMonth() === month &&
    midnight.getDate() === date
    ? midnight
    : null;
}

/** For a day the filters already checked: one that isn't is a mistake in the code. */
function midnight(day: string): Date {
  const date = dateOf(day);
  if (!date) {
    throw new Error(`Not a day: ${day}`);
  }
  return date;
}

function addDays(day: string, days: number): string {
  const date = midnight(day);
  return dayOf(new Date(date.getFullYear(), date.getMonth(), date.getDate() + days));
}

/** An id from the address, or null for none or an empty one. */
function idFrom(value: string | null): string | null {
  return value === null || value === '' ? null : value;
}

/** From midnight of `from` to midnight after `to`, where the person is, at most 366 days, as the API checks. */
export function rangeIsValid(from: string, to: string): boolean {
  const start = dateOf(from);
  const end = dateOf(to);
  if (!start || !end || end < start) {
    return false;
  }
  return midnight(addDays(to, 1)).getTime() - start.getTime() <= MAX_REPORT_DAYS * DAY_MS;
}

/** The last day an interval from `from` may end on; across a change of summer time, a day earlier when it must. */
export function latestTo(from: string): string {
  const latest = addDays(from, MAX_REPORT_DAYS - 1);
  return rangeIsValid(from, latest) ? latest : addDays(latest, -1);
}

/** The first day an interval up to `to` may start on. */
export function earliestFrom(to: string): string {
  const earliest = addDays(to, -(MAX_REPORT_DAYS - 1));
  return rangeIsValid(earliest, to) ? earliest : addDays(earliest, 1);
}

/** "Săptămâna asta" from Monday to Sunday, "Luna asta" from the first to the last day, and the last 30 days, today included. */
export function presetRange(preset: ReportPreset, today: Date): { from: string; to: string } {
  const day = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  switch (preset) {
    case 'week': {
      const monday = new Date(
        day.getFullYear(),
        day.getMonth(),
        day.getDate() - ((day.getDay() + 6) % 7),
      );
      return { from: dayOf(monday), to: addDays(dayOf(monday), 6) };
    }
    case 'month':
      return {
        from: dayOf(new Date(day.getFullYear(), day.getMonth(), 1)),
        to: dayOf(new Date(day.getFullYear(), day.getMonth() + 1, 0)),
      };
    case 'last30':
      return { from: addDays(dayOf(day), -29), to: dayOf(day) };
  }
}

/** The preset an interval is today, or null for one of the person's own. */
export function presetOf(filters: ReportFilters, today: Date): ReportPreset | null {
  const presets: ReportPreset[] = ['week', 'month', 'last30'];
  return (
    presets.find((preset) => {
      const range = presetRange(preset, today);
      return range.from === filters.from && range.to === filters.to;
    }) ?? null
  );
}

/** The project fixed by the route, on a project's own tab, wins over the address. */
export function filtersFromQuery(
  params: ParamMap,
  today: Date,
  fixedProject: string | null,
): ReadFilters {
  const from = params.get('from');
  const to = params.get('to');
  const asked = from !== null || to !== null;
  const given = from !== null && to !== null && rangeIsValid(from, to) ? { from, to } : null;
  return {
    valid: !asked || given !== null,
    filters: {
      ...(given ?? presetRange('last30', today)),
      project: fixedProject ?? idFrom(params.get('project')),
      user: idFrom(params.get('user')),
      granularity: params.get('granularity') === 'month' ? 'month' : 'week',
    },
  };
}

/** The address of these filters, without the defaults: the last 30 days, by the week, and a fixed project. */
export function filtersToQuery(filters: ReportFilters, today: Date, projectFixed: boolean): Params {
  const last30 = presetOf(filters, today) === 'last30';
  return {
    from: last30 ? null : filters.from,
    to: last30 ? null : filters.to,
    project: projectFixed ? null : filters.project,
    user: filters.user,
    granularity: filters.granularity === 'week' ? null : filters.granularity,
  };
}

/** Midnight of the day where the person is, with the offset it has that day: 2026-11-01T00:00:00+02:00. */
export function localInstant(day: string): string {
  const offset = -midnight(day).getTimezoneOffset();
  const sign = offset < 0 ? '-' : '+';
  const hours = String(Math.floor(Math.abs(offset) / 60)).padStart(2, '0');
  const minutes = String(Math.abs(offset) % 60).padStart(2, '0');
  return `${day}T00:00:00${sign}${hours}:${minutes}`;
}

/** What the API takes: [from, to) as instants, `to` being midnight after the last day. */
export function apiInterval(filters: ReportFilters): { from: string; to: string } {
  return { from: localInstant(filters.from), to: localInstant(addDays(filters.to, 1)) };
}
