import { formatHoursMinutes } from '../../shared/models/estimate';

const MINUTES_IN_HOUR = 60;
const MINUTES_IN_DAY = 24 * MINUTES_IN_HOUR;

/**
 * A duration as the reports show it (docs/UI_UX_SPEC.md §7.8): "2 z 4 h" from a day up, "5 h 20 min" under a
 * day, "35 min" under an hour, rounded to the minute first, and "–" when there is nothing to measure.
 */
export function formatDuration(seconds: number | null): string {
  if (seconds === null) {
    return '–';
  }
  const minutes = Math.round(seconds / 60);
  if (minutes >= MINUTES_IN_DAY) {
    const days = Math.floor(minutes / MINUTES_IN_DAY);
    const hours = Math.floor((minutes % MINUTES_IN_DAY) / MINUTES_IN_HOUR);
    return hours === 0 ? $localize`${days} z` : $localize`${days} z ${hours} h`;
  }
  if (minutes >= MINUTES_IN_HOUR) {
    const hours = Math.floor(minutes / MINUTES_IN_HOUR);
    const rest = minutes % MINUTES_IN_HOUR;
    return rest === 0 ? $localize`${hours} h` : $localize`${hours} h ${rest} min`;
  }
  return $localize`${minutes} min`;
}

/**
 * A duration of "Estimare și timp lucrat" (docs/UI_UX_SPEC.md §7.8, group 23): hours of the work schedule, so
 * hours and minutes however many, never days ("40 h", "2 h 30 min", "35 min"), rounded to the minute first,
 * and "–" for nothing to measure.
 */
export function formatWorkDuration(seconds: number | null): string {
  return seconds === null ? '–' : formatHoursMinutes(Math.round(seconds / 60));
}
