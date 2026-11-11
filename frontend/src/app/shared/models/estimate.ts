/**
 * A task's estimate and worked time (docs/SPEC.md §4.4, docs/UI_UX_SPEC.md §7.4 and §7.5): the API keeps whole
 * minutes, 15 to 60,000, which people give in hours, 0.25 to 1000, with a comma or a dot; the worked time
 * comes in seconds of the work schedule and shows in hours and minutes, never in days.
 */
export const MIN_ESTIMATE_HOURS = 0.25;
export const MAX_ESTIMATE_HOURS = 1000;
/** What the field's arrows add or take away. */
export const ESTIMATE_STEP_HOURS = 0.25;

/** Hours as typed, "1,5" or "1.5": null for anything that isn't a plain number. */
export function parseHours(text: string): number | null {
  const plain = text.trim().replace(',', '.');
  return /^(\d+(\.\d*)?|\.\d+)$/.test(plain) ? Number(plain) : null;
}

/** The problem with hours as typed, or null when they make an estimate. */
export function estimateProblem(text: string): string | null {
  const hours = parseHours(text);
  return hours === null || hours < MIN_ESTIMATE_HOURS || hours > MAX_ESTIMATE_HOURS
    ? $localize`Între 0,25 și 1000 de ore.`
    : null;
}

/** Hours as the API's whole minutes. */
export function hoursToMinutes(hours: number): number {
  return Math.round(hours * 60);
}

/** Minutes as the field shows them, in hours with a comma: 90 is "1,5". */
export function minutesToHoursText(minutes: number): string {
  return String(Math.round((minutes / 60) * 100) / 100).replace('.', ',');
}

/** "4 h", "2 h 10 min", "45 min" or "0 min": hours and minutes, never days. */
export function formatHoursMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return $localize`${rest}:minutes: min`;
  }
  return rest === 0
    ? $localize`${hours}:hours: h`
    : $localize`${hours}:hours: h ${rest}:minutes: min`;
}

/** The worked seconds as whole minutes gone by, so a timer never shows a minute it hasn't reached. */
export function formatWorked(seconds: number): string {
  return formatHoursMinutes(Math.floor(seconds / 60));
}

/** Past its estimate: the worked time is over the estimated one. */
export function isOverEstimate(workedSeconds: number, estimatedMinutes: number | null): boolean {
  return estimatedMinutes !== null && workedSeconds > estimatedMinutes * 60;
}
