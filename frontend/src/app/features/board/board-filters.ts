import { TaskFilters } from '../../shared/models/task-filters';
import { BoardColumnDto, BoardTaskDto } from './models';

/**
 * Which cards the filters let through (docs/UI_UX_SPEC.md §7.4), all of them holding at once, in the
 * browser: the board has every open task already. "me" is whoever is looking. Late means open past
 * the deadline, outside a done column, as the API counts it. The search finds a part of the title.
 */
export function cardFilter(
  filters: TaskFilters,
  columns: readonly BoardColumnDto[],
  me: string | null,
  now: Date,
): (task: BoardTaskDto) => boolean {
  const assignee = filters.assignee === 'me' ? me : filters.assignee;
  const priorities = new Set(filters.priorities);
  const search = filters.search.trim().toLocaleLowerCase('ro-RO');
  const done = new Set(columns.filter((column) => column.isDone).map((column) => column.id));
  return (task) =>
    (assignee === null || task.assignee?.userId === assignee) &&
    (priorities.size === 0 || priorities.has(task.priority)) &&
    (!filters.overdue ||
      (task.dueDate !== null &&
        new Date(task.dueDate) < now &&
        !done.has(task.columnId) &&
        task.completedAt === null)) &&
    (search === '' || task.title.toLocaleLowerCase('ro-RO').includes(search));
}
