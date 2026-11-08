import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { PageEvent } from '@angular/material/paginator';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import {
  NO_TASK_FILTERS,
  TaskFilters,
  TaskSort,
  TaskSortField,
  filtersFromQuery,
  filtersToQuery,
  hasFilters,
  sortFromQuery,
  sortToQuery,
} from '../../shared/models/task-filters';
import { TaskChanges } from '../../shared/task-changes';
import { openTaskPanel } from '../../shared/task-panel';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { TaskFilterBar } from '../../shared/ui/task-filter-bar';
import { MyTasksStore } from './my-tasks-store';
import { MY_TASKS_SORT, MyTasksQuery } from './tasks-api';
import { TaskRow, TaskTable } from './task-table';

const PAGE_SIZES = [25, 50, 100];
const DEFAULT_PAGE_SIZE = 50;

/**
 * "Task-urile mele" at /my-tasks (docs/UI_UX_SPEC.md §7.2): the project's List view across every project,
 * with a "Proiect" column and no assignee to pick, only the open tasks unless "Și cele terminate" (done=1)
 * says otherwise, by deadline from the soonest, 50 to a page. Filters, sort and page live in the address,
 * as on the List; a row opens the task's panel, and what the panel changes reads the page again.
 */
@Component({
  selector: 'kb-my-tasks-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MyTasksStore],
  imports: [MatButton, EmptyState, PageHeader, Skeleton, TaskFilterBar, TaskTable],
  templateUrl: './my-tasks-page.html',
  styleUrl: './my-tasks-page.scss',
})
export class MyTasksPage {
  protected readonly store = inject(MyTasksStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly changes = inject(TaskChanges);

  protected readonly heading = $localize`Task-urile mele`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Task-urile mele` },
  ];
  protected readonly columns = [
    'key',
    'title',
    'project',
    'column',
    'priority',
    'dueDate',
    'assignee',
    'updated',
  ];
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly loadFailed = $localize`Nu am putut încărca task-urile.`;
  protected readonly emptyTitle = $localize`Niciun task deschis.`;
  protected readonly noMatchTitle = $localize`Niciun task pentru filtrele alese.`;

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: convertToParamMap({}),
  });

  protected readonly filters = computed<TaskFilters>(() => filtersFromQuery(this.params()));
  protected readonly finished = computed(() => this.params().get('done') === '1');
  protected readonly sort = computed<TaskSort>(() => sortFromQuery(this.params(), MY_TASKS_SORT));
  protected readonly page = computed(() => positive(this.params().get('page'), 1));
  protected readonly pageSize = computed(() => {
    const size = positive(this.params().get('size'), DEFAULT_PAGE_SIZE);
    return PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE;
  });
  protected readonly filtered = computed(() => hasFilters(this.filters()));

  private readonly query = computed<MyTasksQuery>(() => {
    const filters = this.filters();
    return {
      page: this.page(),
      pageSize: this.pageSize(),
      sort: this.sort(),
      open: !this.finished(),
      priorities: filters.priorities,
      overdue: filters.overdue,
      search: filters.search,
      includeArchived: filters.archived,
    };
  });

  constructor() {
    // Only the address and the panel's changes start a load: what load reads mustn't start another.
    effect(() => {
      const query = this.query();
      this.changes.version();
      untracked(() => {
        void this.store.load(query);
      });
    });
  }

  /** The assignee isn't a filter here: every task is the user's own. */
  protected filter(filters: TaskFilters): void {
    void this.navigate({ ...filtersToQuery({ ...filters, assignee: null }), page: null });
  }

  protected toggleFinished(finished: boolean): void {
    void this.navigate({ done: finished ? '1' : null, page: null });
  }

  /** The finished and archived ones stay as chosen: they add tasks, they don't narrow them. */
  protected clearFilters(): void {
    this.filter({ ...NO_TASK_FILTERS, archived: this.filters().archived });
  }

  /** The same column again turns the order around; another one starts from its first value. */
  protected sortBy(field: TaskSortField): void {
    const current = this.sort();
    const sort: TaskSort =
      current.field === field
        ? { field, descending: !current.descending }
        : { field, descending: field === 'updated' };
    void this.navigate({ sort: sortToQuery(sort, MY_TASKS_SORT), page: null });
  }

  protected paged(event: PageEvent): void {
    void this.navigate({
      page: event.pageIndex === 0 ? null : event.pageIndex + 1,
      size: event.pageSize === DEFAULT_PAGE_SIZE ? null : event.pageSize,
    });
  }

  protected open(task: TaskRow): void {
    void openTaskPanel(this.router, task.key);
  }

  private navigate(queryParams: Record<string, string | number | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }
}

function positive(value: string | null, fallback: number): number {
  const number = Number(value);
  return Number.isInteger(number) && number > 0 ? number : fallback;
}
