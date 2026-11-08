import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { MyTaskDto } from './models';
import { MyTasksQuery, TasksApi } from './tasks-api';

export type MyTasksState = 'loading' | 'ready' | 'error';

/**
 * "Task-urile mele" (docs/UI_UX_SPEC.md §7.2): a page of the user's own tasks across every project, as
 * the address asks for it. Only the last page asked for lands, however the answers arrive.
 */
@Injectable()
export class MyTasksStore {
  private readonly api = inject(TasksApi);
  private readonly auth = inject(AuthStore);
  private latest = 0;
  private lastQuery: MyTasksQuery | null = null;

  private readonly rowList = signal<MyTaskDto[]>([]);
  private readonly total = signal(0);
  private readonly loadState = signal<MyTasksState>('loading');

  readonly rows = this.rowList.asReadonly();
  readonly totalCount = this.total.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly canSeeArchived = computed(() => this.auth.has(PERMISSIONS.tasksArchive));

  async load(query: MyTasksQuery): Promise<void> {
    this.lastQuery = query;
    const asked = ++this.latest;
    this.loadState.set('loading');
    try {
      const page = await firstValueFrom(
        this.api.myTasks({
          ...query,
          includeArchived: query.includeArchived && this.canSeeArchived(),
        }),
      );
      if (asked === this.latest) {
        this.rowList.set(page.items);
        this.total.set(page.totalCount);
        this.loadState.set('ready');
      }
    } catch {
      if (asked === this.latest) {
        this.loadState.set('error');
      }
    }
  }

  reload(): Promise<void> {
    return this.lastQuery ? this.load(this.lastQuery) : Promise.resolve();
  }
}
