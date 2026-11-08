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
import { MatDialog } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
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
import { openTaskPanel } from '../../shared/task-panel';
import { EmptyState } from '../../shared/ui/empty-state';
import { SHEET_WIDTH } from '../../shared/ui/sheet';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskFilterBar } from '../../shared/ui/task-filter-bar';
import { TaskItemDto } from './models';
import { TaskChanges } from '../../shared/task-changes';
import { TaskListStore } from './task-list-store';
import { TaskSheet } from './task-sheet';
import { TaskRow, TaskTable } from './task-table';

const PAGE_SIZES = [25, 50, 100];
const DEFAULT_PAGE_SIZE = 50;

/**
 * The List view of a project (docs/UI_UX_SPEC.md §7.3): its tasks as a table, by key from the newest,
 * sorted by any column, with the board's filters. Filters, sort and page live in the address, so a
 * list can be shared and the back button goes back through them. A row opens the task's panel over
 * the list; what the panel changes reads the page again.
 */
@Component({
  selector: 'kb-task-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TaskListStore],
  imports: [MatButton, EmptyState, Skeleton, TaskFilterBar, TaskTable],
  templateUrl: './task-list-page.html',
  styleUrl: './task-list-page.scss',
})
export class TaskListPage {
  protected readonly store = inject(TaskListStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snackbar = inject(Snackbar);
  private readonly changes = inject(TaskChanges);

  protected readonly columns = [
    'key',
    'title',
    'column',
    'priority',
    'dueDate',
    'assignee',
    'updated',
  ];
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly loadFailed = $localize`Nu am putut încărca task-urile.`;
  protected readonly emptyTitle = $localize`Niciun task încă`;
  protected readonly noMatchTitle = $localize`Niciun task nu se potrivește`;

  private readonly projectId = toSignal(
    (this.route.parent ?? this.route).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: '' },
  );
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: convertToParamMap({}),
  });

  protected readonly filters = computed<TaskFilters>(() => filtersFromQuery(this.query()));
  protected readonly sort = computed<TaskSort>(() => sortFromQuery(this.query()));
  protected readonly page = computed(() => positive(this.query().get('page'), 1));
  protected readonly pageSize = computed(() => {
    const size = positive(this.query().get('size'), DEFAULT_PAGE_SIZE);
    return PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE;
  });
  protected readonly filtered = computed(() => hasFilters(this.filters()));
  protected readonly people = computed(() =>
    this.store.members().filter((member) => member.isActive),
  );

  constructor() {
    // Only the address and the panel's changes start a load: what load reads mustn't start another.
    effect(() => {
      const projectId = this.projectId();
      const request = {
        projectId,
        filters: this.filters(),
        sort: this.sort(),
        page: this.page(),
        pageSize: this.pageSize(),
      };
      this.changes.version();
      if (projectId !== '') {
        untracked(() => {
          void this.store.loadProject(projectId);
          void this.store.load(request);
        });
      }
    });
  }

  protected filter(filters: TaskFilters): void {
    void this.navigate({ ...filtersToQuery(filters), page: null });
  }

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
    void this.navigate({ sort: sortToQuery(sort), page: null });
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

  protected retry(): void {
    void this.store.load({
      projectId: this.projectId(),
      filters: this.filters(),
      sort: this.sort(),
      page: this.page(),
      pageSize: this.pageSize(),
    });
  }

  protected async create(): Promise<void> {
    const ref = this.dialog.open<TaskSheet, TaskListStore, TaskItemDto>(TaskSheet, {
      data: this.store,
      width: SHEET_WIDTH.form,
      ariaLabelledBy: 'kb-sheet-title',
    });
    const created = await firstValueFrom(ref.afterClosed());
    if (created) {
      this.snackbar.show($localize`Task-ul ${created.key}:key: a fost creat.`);
      this.changes.saved();
    }
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
