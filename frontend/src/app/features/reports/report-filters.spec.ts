import { convertToParamMap } from '@angular/router';
import {
  apiInterval,
  dayOf,
  earliestFrom,
  filtersFromQuery,
  filtersToQuery,
  latestTo,
  localInstant,
  presetOf,
  presetRange,
  rangeIsValid,
} from './report-filters';

// Thursday 5 November 2026, at noon where the specs run.
const TODAY = new Date(2026, 10, 5, 12, 0, 0);

/**
 * docs/UI_UX_SPEC.md §7.8: the filters live in the address, from and to as days, both included, the
 * presets computed from today, and a wrong interval sends the page back to the last 30 days. The API gets
 * midnight where the person is, with its offset, and the day after `to`.
 */
describe('report filters', () => {
  it('reads from, to, project, user and granularity from the address', () => {
    const read = filtersFromQuery(
      convertToParamMap({
        from: '2026-10-01',
        to: '2026-10-31',
        project: 'p1',
        user: 'u2',
        granularity: 'month',
      }),
      TODAY,
      null,
    );

    expect(read).toEqual({
      valid: true,
      filters: {
        from: '2026-10-01',
        to: '2026-10-31',
        project: 'p1',
        user: 'u2',
        granularity: 'month',
      },
    });
  });

  it('opens on the last 30 days, today included, by the week', () => {
    expect(filtersFromQuery(convertToParamMap({}), TODAY, null)).toEqual({
      valid: true,
      filters: {
        from: '2026-10-07',
        to: '2026-11-05',
        project: null,
        user: null,
        granularity: 'week',
      },
    });
  });

  it.each([
    { query: { from: '2026-10-31', to: '2026-10-01' }, reason: 'to before from' },
    { query: { from: '2025-10-01', to: '2026-10-31' }, reason: 'over 366 days' },
    { query: { from: 'luni', to: '2026-10-31' }, reason: 'a day that is not one' },
    { query: { from: '2026-02-30', to: '2026-03-31' }, reason: 'a day that does not exist' },
    { query: { from: '2026-10-01' }, reason: 'from without to' },
    { query: { to: '2026-10-01' }, reason: 'to without from' },
  ])('takes $query as a wrong interval: $reason', ({ query }) => {
    const read = filtersFromQuery(convertToParamMap(query), TODAY, null);

    expect(read.valid).toBe(false);
    expect([read.filters.from, read.filters.to]).toEqual(['2026-10-07', '2026-11-05']);
  });

  it('takes a granularity it does not know as the week', () => {
    expect(
      filtersFromQuery(convertToParamMap({ granularity: 'zi' }), TODAY, null).filters.granularity,
    ).toBe('week');
  });

  it('keeps the project fixed by the route, whatever the address says', () => {
    const read = filtersFromQuery(convertToParamMap({ project: 'p9' }), TODAY, 'p1');

    expect(read.filters.project).toBe('p1');
  });

  it('accepts a single day, and 366 days', () => {
    expect(rangeIsValid('2026-11-05', '2026-11-05')).toBe(true);
    expect(rangeIsValid('2026-01-01', '2027-01-01')).toBe(true);
    expect(rangeIsValid('2026-01-01', '2027-01-02')).toBe(false);
  });

  it('gives this week from Monday to Sunday, this month from the first to the last day', () => {
    expect(presetRange('week', TODAY)).toEqual({ from: '2026-11-02', to: '2026-11-08' });
    expect(presetRange('month', TODAY)).toEqual({ from: '2026-11-01', to: '2026-11-30' });
    expect(presetRange('last30', TODAY)).toEqual({ from: '2026-10-07', to: '2026-11-05' });
  });

  it('takes a Sunday back to its own Monday', () => {
    expect(presetRange('week', new Date(2026, 10, 8, 23, 0, 0))).toEqual({
      from: '2026-11-02',
      to: '2026-11-08',
    });
  });

  it('says which preset an interval is, and none for one of its own', () => {
    const base = { project: null, user: null, granularity: 'week' as const };

    expect(presetOf({ ...base, from: '2026-11-02', to: '2026-11-08' }, TODAY)).toBe('week');
    expect(presetOf({ ...base, from: '2026-11-01', to: '2026-11-30' }, TODAY)).toBe('month');
    expect(presetOf({ ...base, from: '2026-10-07', to: '2026-11-05' }, TODAY)).toBe('last30');
    expect(presetOf({ ...base, from: '2026-10-01', to: '2026-10-31' }, TODAY)).toBeNull();
  });

  it('writes the address without the defaults', () => {
    const base = { project: null, user: null, granularity: 'week' as const };

    expect(filtersToQuery({ ...base, from: '2026-10-07', to: '2026-11-05' }, TODAY, false)).toEqual(
      {
        from: null,
        to: null,
        project: null,
        user: null,
        granularity: null,
      },
    );
    expect(
      filtersToQuery(
        { from: '2026-10-01', to: '2026-10-31', project: 'p1', user: 'u2', granularity: 'month' },
        TODAY,
        false,
      ),
    ).toEqual({
      from: '2026-10-01',
      to: '2026-10-31',
      project: 'p1',
      user: 'u2',
      granularity: 'month',
    });
  });

  it('leaves the project out of the address when the route fixes it', () => {
    expect(
      filtersToQuery(
        { from: '2026-10-01', to: '2026-10-31', project: 'p1', user: null, granularity: 'week' },
        TODAY,
        true,
      )['project'],
    ).toBeNull();
  });

  it('sends midnight where the person is, with its offset, and the day after to', () => {
    const interval = apiInterval({
      from: '2026-10-01',
      to: '2026-10-31',
      project: null,
      user: null,
      granularity: 'week',
    });

    expect(interval.from).toMatch(/^2026-10-01T00:00:00[+-]\d\d:\d\d$/);
    expect(interval.to).toMatch(/^2026-11-01T00:00:00[+-]\d\d:\d\d$/);
    expect(new Date(interval.from).getTime()).toBe(new Date(2026, 9, 1).getTime());
    expect(new Date(interval.to).getTime()).toBe(new Date(2026, 10, 1).getTime());
  });

  it('writes a moment with the offset it has on that day', () => {
    const offset = -new Date(2026, 6, 1).getTimezoneOffset();
    const sign = offset < 0 ? '-' : '+';
    const hours = String(Math.floor(Math.abs(offset) / 60)).padStart(2, '0');
    const minutes = String(Math.abs(offset) % 60).padStart(2, '0');

    expect(localInstant('2026-07-01')).toBe(`2026-07-01T00:00:00${sign}${hours}:${minutes}`);
  });

  it('bounds the datepickers to 366 days either way', () => {
    expect(latestTo('2026-01-01')).toBe('2027-01-01');
    expect(earliestFrom('2027-01-01')).toBe('2026-01-01');
    expect(rangeIsValid('2026-01-01', latestTo('2026-01-01'))).toBe(true);
  });

  it('writes a day as the person sees it', () => {
    expect(dayOf(new Date(2026, 0, 9, 23, 59))).toBe('2026-01-09');
  });
});
