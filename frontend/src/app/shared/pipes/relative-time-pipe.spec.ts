import { fullDate, relativeTime } from './relative-time-pipe';

describe('relativeTime', () => {
  const now = new Date(2026, 10, 6, 12, 0, 0).getTime();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();
  const minutes = 60_000;
  const hours = 60 * minutes;

  it('says now under a minute, and for a time ahead of the clock', () => {
    expect(relativeTime(ago(30_000), now)).toBe('acum');
    expect(relativeTime(ago(-5 * minutes), now)).toBe('acum');
  });

  it('counts minutes, then hours', () => {
    expect(relativeTime(ago(5 * minutes), now)).toBe('acum 5 minute');
    expect(relativeTime(ago(20 * minutes), now)).toBe('acum 20 de minute');
    expect(relativeTime(ago(2 * hours), now)).toBe('acum 2 ore');
  });

  it('counts hours for a day, then calendar days, so 27 hours ago is yesterday', () => {
    expect(relativeTime(new Date(2026, 10, 5, 23, 0).toISOString(), now)).toBe('acum 13 ore');
    expect(relativeTime(new Date(2026, 10, 5, 9, 0).toISOString(), now)).toBe('ieri');
    expect(relativeTime(new Date(2026, 10, 4, 23, 0).toISOString(), now)).toBe('alaltăieri');
    expect(relativeTime(new Date(2026, 10, 3, 9, 0).toISOString(), now)).toBe('acum 3 zile');
  });

  it('gives the date from 7 days on', () => {
    expect(relativeTime(new Date(2026, 9, 30, 9, 0).toISOString(), now)).toBe('30.10.2026');
  });

  it('writes the full date for the tooltip', () => {
    expect(fullDate(new Date(2026, 10, 4, 8, 5).toISOString())).toBe('04.11.2026 08:05');
  });
});
