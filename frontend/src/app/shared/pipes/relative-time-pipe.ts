import { formatDate } from '@angular/common';
import { Pipe, PipeTransform } from '@angular/core';

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
const WEEK = 7 * DAY;

const RELATIVE = new Intl.RelativeTimeFormat('ro-RO', { numeric: 'auto' });

/**
 * A time as docs/UI_UX_SPEC.md §11 writes it: relative under 7 days ("acum 5 minute", "ieri",
 * "acum 3 zile"), the date after that. A time ahead of the clock, from a clock that runs behind,
 * counts as now. The full date goes in a tooltip next to it: fullDate.
 */
export function relativeTime(iso: string, now: number = Date.now()): string {
  const at = Date.parse(iso);
  const ago = Math.max(0, now - at);
  if (ago >= WEEK) {
    return formatDate(at, 'dd.MM.yyyy', 'ro-RO');
  }
  if (ago < MINUTE) {
    return RELATIVE.format(0, 'second');
  }
  if (ago < HOUR) {
    return RELATIVE.format(-Math.floor(ago / MINUTE), 'minute');
  }
  if (ago < DAY) {
    return RELATIVE.format(-Math.floor(ago / HOUR), 'hour');
  }
  return RELATIVE.format(-calendarDaysBetween(at, now), 'day');
}

/** The date and time a relative time stands for. */
export function fullDate(iso: string): string {
  return formatDate(iso, 'dd.MM.yyyy HH:mm', 'ro-RO');
}

/** Yesterday is the day before today on the calendar, not 24 hours ago. */
function calendarDaysBetween(earlier: number, later: number): number {
  const startOf = (time: number) => new Date(time).setHours(0, 0, 0, 0);
  return Math.max(1, Math.round((startOf(later) - startOf(earlier)) / DAY));
}

@Pipe({ name: 'relativeTime' })
export class RelativeTimePipe implements PipeTransform {
  transform(iso: string): string {
    return relativeTime(iso);
  }
}
