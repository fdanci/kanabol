import { formatHoursMinutes } from '../../shared/models/estimate';
import { PRIORITIES, TaskHistoryEntryDto, TaskPriority, priorityLabel } from './models';

/**
 * A change as the timeline writes it (docs/UI_UX_SPEC.md §7.5): «**Florin** a schimbat *Prioritate*:
 * Medium → High». Most fields are 'changed', with the field's label and old → new; the description,
 * an archiving and a restoring are sentences of their own, the description without its texts, and so are
 * a first estimate («a estimat task-ul la 4 h») and a pause with its reason (group 22).
 */
export interface HistoryLine {
  who: string;
  kind: 'changed' | 'description' | 'archived' | 'restored' | 'estimated' | 'paused';
  field: string;
  change: string;
}

export function historyLine(entry: TaskHistoryEntryDto): HistoryLine {
  const who = entry.changedBy.displayName;
  const changed = (field: string, show: (value: string | null) => string): HistoryLine => ({
    who,
    kind: 'changed',
    field,
    change: `${show(entry.oldValue)} → ${show(entry.newValue)}`,
  });

  switch (entry.field) {
    case 'Title':
      return changed($localize`Titlu`, (value) => value ?? '');
    case 'Priority':
      return changed($localize`Prioritate`, priorityText);
    case 'DueDate':
      return changed($localize`Deadline`, dueDateText);
    case 'ColumnId':
      return changed($localize`Stare`, (value) =>
        value ? (entry.names[value] ?? $localize`coloană ștearsă`) : '—',
      );
    case 'Assignees':
      return {
        who,
        kind: 'changed',
        field: $localize`Asignați`,
        change: assigneesChange(
          entry.oldValue,
          entry.newValue,
          (userId) => entry.names[userId] ?? undefined,
        ),
      };
    case 'EstimatedMinutes':
      return entry.oldValue === null
        ? { who, kind: 'estimated', field: '', change: minutesText(entry.newValue) }
        : changed($localize`Estimarea`, minutesText);
    case 'PauseReason':
      return { who, kind: 'paused', field: '', change: entry.newValue ?? '' };
    case 'Description':
      return { who, kind: 'description', field: '', change: '' };
    case 'ArchivedAt':
      return { who, kind: entry.newValue ? 'archived' : 'restored', field: '', change: '' };
    default:
      // A field this version doesn't know yet: its name, and its values as they are.
      return changed(entry.field, (value) => (value === null || value === '' ? '—' : value));
  }
}

/**
 * A change of assignees: «Ana → Ana, Dan». The history entry holds the ids, sorted and separated by
 * commas, empty for nobody (docs/SPEC.md §5.1), and GET /api/tasks/{id}/history gives their names,
 * also for someone who left the project; the names come by name whatever the case.
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

/** Minutes as hours and minutes, "—" for none. */
function minutesText(value: string | null): string {
  const minutes = value === null || value === '' ? NaN : Number(value);
  return Number.isNaN(minutes) ? '—' : formatHoursMinutes(minutes);
}

/** By the label the UI gives it, or as it is for one the UI doesn't have. */
function priorityText(value: string | null): string {
  if (!value) {
    return '—';
  }
  return PRIORITIES.includes(value as TaskPriority) ? priorityLabel(value as TaskPriority) : value;
}

/** The day the deadline falls on where the person is, as the panel shows it, in ro-RO. */
function dueDateText(value: string | null): string {
  const day = value ? new Date(value) : null;
  return day && !Number.isNaN(day.getTime())
    ? day.toLocaleDateString('ro-RO')
    : $localize`fără deadline`;
}
