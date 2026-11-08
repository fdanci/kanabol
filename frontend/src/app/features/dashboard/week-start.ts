/**
 * The Monday of the week at midnight where the person is, as an instant (docs/SPEC.md §4.4): the API counts
 * the tasks finished since then. Sunday ends the week, so it goes back six days.
 */
export function weekStart(now: Date): string {
  const daysSinceMonday = (now.getDay() + 6) % 7;
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() - daysSinceMonday).toISOString();
}
