import { weekStart } from './week-start';

/** docs/SPEC.md §4.4: the client sends the Monday at midnight in its own time, as an instant. */
describe('weekStart', () => {
  it('goes back to Monday at midnight, where the person is', () => {
    const thursday = new Date(2026, 10, 5, 15, 30);

    expect(weekStart(thursday)).toBe(new Date(2026, 10, 2, 0, 0, 0, 0).toISOString());
  });

  it('keeps a Monday, from its own midnight', () => {
    expect(weekStart(new Date(2026, 10, 2, 0, 0, 1))).toBe(new Date(2026, 10, 2).toISOString());
  });

  // Sunday is the end of the week, not its start.
  it('takes a Sunday back six days', () => {
    expect(weekStart(new Date(2026, 10, 8, 23, 59))).toBe(new Date(2026, 10, 2).toISOString());
  });

  it('crosses a month', () => {
    expect(weekStart(new Date(2026, 10, 1, 12, 0))).toBe(new Date(2026, 9, 26).toISOString());
  });
});
