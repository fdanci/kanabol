import {
  estimateProblem,
  formatHoursMinutes,
  formatWorked,
  hoursToMinutes,
  isOverEstimate,
  minutesToHoursText,
  parseHours,
} from './estimate';

describe('estimate', () => {
  it('reads hours with a comma or a dot, and nothing else', () => {
    expect(parseHours('1,5')).toBe(1.5);
    expect(parseHours(' 2.25 ')).toBe(2.25);
    expect(parseHours('4')).toBe(4);
    expect(parseHours(',5')).toBe(0.5);
    for (const text of ['', 'doi', '1,5 h', '-1', '1e3', '1,2,3']) {
      expect(parseHours(text)).toBeNull();
    }
  });

  // docs/UI_UX_SPEC.md §7.4: 0.25 to 1000 hours, or "Între 0,25 și 1000 de ore."
  it('takes 0.25 to 1000 hours, and says so otherwise', () => {
    expect(estimateProblem('0,25')).toBeNull();
    expect(estimateProblem('1000')).toBeNull();
    for (const text of ['0,2', '1000,5', '', 'mult']) {
      expect(estimateProblem(text)).toBe('Între 0,25 și 1000 de ore.');
    }
  });

  it('turns hours into the API whole minutes, and back into the field text', () => {
    expect(hoursToMinutes(0.25)).toBe(15);
    expect(hoursToMinutes(1000)).toBe(60_000);
    expect(hoursToMinutes(1.1)).toBe(66);
    expect(minutesToHoursText(90)).toBe('1,5');
    expect(minutesToHoursText(240)).toBe('4');
  });

  it('writes a duration in hours and minutes, never in days', () => {
    expect(formatHoursMinutes(240)).toBe('4 h');
    expect(formatHoursMinutes(130)).toBe('2 h 10 min');
    expect(formatHoursMinutes(45)).toBe('45 min');
    expect(formatHoursMinutes(0)).toBe('0 min');
    expect(formatHoursMinutes(60 * 30)).toBe('30 h');
  });

  it('writes the worked time from the minutes gone by', () => {
    expect(formatWorked(59)).toBe('0 min');
    expect(formatWorked(7_800)).toBe('2 h 10 min');
    expect(formatWorked(7_859)).toBe('2 h 10 min');
  });

  it('is over the estimate only past it', () => {
    expect(isOverEstimate(14_400, 240)).toBe(false);
    expect(isOverEstimate(14_401, 240)).toBe(true);
    expect(isOverEstimate(99_999, null)).toBe(false);
  });
});
