/**
 * A change of assignees, as the timeline writes it (docs/UI_UX_SPEC.md §7.5): «Ana → Ana, Dan». The
 * history entry holds the ids, sorted and separated by commas, empty for nobody (docs/SPEC.md §5.1),
 * so the names come from whoever the page knows, by name whatever the case. The timeline itself is
 * group 16's, with GET /api/tasks/{id}/history.
 */
export function assigneesChange(
  oldValue: string | null,
  newValue: string | null,
  nameOf: (userId: string) => string | undefined,
): string {
  return `${namesIn(oldValue, nameOf)} → ${namesIn(newValue, nameOf)}`;
}

function namesIn(value: string | null, nameOf: (userId: string) => string | undefined): string {
  const ids = (value ?? '').split(',').filter((id) => id !== '');
  if (ids.length === 0) {
    return $localize`Neasignat`;
  }
  return ids
    .map((id) => nameOf(id) ?? $localize`un fost membru`)
    .sort((a, b) => a.localeCompare(b, 'ro-RO', { sensitivity: 'base' }))
    .join(', ');
}
