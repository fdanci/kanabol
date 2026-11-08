import { ParamMap, Params } from '@angular/router';

/** The four priorities, lowest first, as the API writes them (docs/SPEC.md §5.1). */
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export const PRIORITIES: readonly TaskPriority[] = ['Low', 'Medium', 'High', 'Critical'];

/** Their names on the chips and in the menus: the team's words, so they stay as they are. */
export function priorityLabel(priority: TaskPriority): string {
  switch (priority) {
    case 'Low':
      return $localize`:@@priorityLow:Low`;
    case 'Medium':
      return $localize`:@@priorityMedium:Medium`;
    case 'High':
      return $localize`:@@priorityHigh:High`;
    case 'Critical':
      return $localize`:@@priorityCritical:Critical`;
  }
}

/**
 * The filters of the board (docs/UI_UX_SPEC.md §7.4), which the List view has too, all holding at
 * once. The assignee is a user's id, or "me", so a link with Ale mele means the tasks of whoever opens it.
 */
export interface TaskFilters {
  assignee: string | null;
  priorities: TaskPriority[];
  overdue: boolean;
  search: string;
  /** Archived tasks too, for tasks.archive. */
  archived: boolean;
}

export const NO_TASK_FILTERS: TaskFilters = {
  assignee: null,
  priorities: [],
  overdue: false,
  search: '',
  archived: false,
};

export type TaskSortField =
  'key' | 'title' | 'column' | 'priority' | 'dueDate' | 'assignee' | 'updated';

export interface TaskSort {
  field: TaskSortField;
  descending: boolean;
}

/** By key from the newest (docs/UI_UX_SPEC.md §7.3). */
export const DEFAULT_TASK_SORT: TaskSort = { field: 'key', descending: true };

const SORT_FIELDS: readonly TaskSortField[] = [
  'key',
  'title',
  'column',
  'priority',
  'dueDate',
  'assignee',
  'updated',
];

export function filtersFromQuery(params: ParamMap): TaskFilters {
  const named = new Set((params.get('priority') ?? '').split(','));
  const assignee = params.get('assignee');
  return {
    assignee: assignee === '' ? null : assignee,
    priorities: PRIORITIES.filter((priority) => named.has(priority)),
    overdue: params.get('overdue') === '1',
    search: params.get('q') ?? '',
    archived: params.get('archived') === '1',
  };
}

/** Null for a filter that's off, so merging the query takes it out of the address. */
export function filtersToQuery(filters: TaskFilters): Params {
  return {
    assignee: filters.assignee,
    priority: filters.priorities.length > 0 ? filters.priorities.join(',') : null,
    overdue: filters.overdue ? '1' : null,
    q: filters.search.trim() === '' ? null : filters.search.trim(),
    archived: filters.archived ? '1' : null,
  };
}

/** Whether something narrows the tasks down: archived ones on their own add tasks, they don't hide any. */
export function hasFilters(filters: TaskFilters): boolean {
  return (
    filters.assignee !== null ||
    filters.priorities.length > 0 ||
    filters.overdue ||
    filters.search.trim() !== ''
  );
}

/** The page's own default when the address says nothing, or nothing it knows. */
export function sortFromQuery(params: ParamMap, fallback: TaskSort = DEFAULT_TASK_SORT): TaskSort {
  const [field, direction] = (params.get('sort') ?? '').split(':');
  return SORT_FIELDS.includes(field as TaskSortField) &&
    (direction === 'asc' || direction === 'desc')
    ? { field: field as TaskSortField, descending: direction === 'desc' }
    : fallback;
}

/** As the API takes it, field:asc or field:desc, and null for the default, which the address leaves out. */
export function sortToQuery(sort: TaskSort, fallback: TaskSort = DEFAULT_TASK_SORT): string | null {
  return sort.field === fallback.field && sort.descending === fallback.descending
    ? null
    : `${sort.field}:${sort.descending ? 'desc' : 'asc'}`;
}
