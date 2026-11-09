import { formatDuration } from './durations';

const MINUTE = 60;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** docs/UI_UX_SPEC.md §7.8: durations as "2 z 4 h", under an hour "35 min", "–" for none. */
describe('formatDuration', () => {
  it('writes days and hours', () => {
    expect(formatDuration(2 * DAY + 4 * HOUR + 20 * MINUTE)).toBe('2 z 4 h');
  });

  it('writes whole days without hours', () => {
    expect(formatDuration(3 * DAY)).toBe('3 z');
  });

  it('writes hours and minutes under a day', () => {
    expect(formatDuration(5 * HOUR + 20 * MINUTE)).toBe('5 h 20 min');
    expect(formatDuration(5 * HOUR)).toBe('5 h');
  });

  it('writes minutes under an hour', () => {
    expect(formatDuration(35 * MINUTE)).toBe('35 min');
    expect(formatDuration(0)).toBe('0 min');
  });

  it('rounds to the nearest minute first', () => {
    expect(formatDuration(59 * MINUTE + 31)).toBe('1 h');
    expect(formatDuration(DAY - 20)).toBe('1 z');
  });

  it('writes a dash for nothing to measure', () => {
    expect(formatDuration(null)).toBe('–');
  });
});
