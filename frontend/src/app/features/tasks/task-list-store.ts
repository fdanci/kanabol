import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { TaskFilters, TaskSort } from '../../shared/models/task-filters';
import { NewTask, TaskItemDto, TaskMemberDto, TaskProjectDto, TaskSummaryDto } from './models';
import { TasksApi } from './tasks-api';

export type TaskListState = 'loading' | 'ready' | 'error';

export interface TaskListRequest {
  projectId: string;
  filters: TaskFilters;
  sort: TaskSort;
  page: number;
  pageSize: number;
}

/**
 * The List view of a project (docs/UI_UX_SPEC.md §7.3): a page of its tasks, as the address asks for
 * them, and what the page needs of the project, its columns and members, for the filters and Task nou.
 */
@Injectable()
export class TaskListStore {
  private readonly api = inject(TasksApi);
  private readonly auth = inject(AuthStore);
  private latest = 0;

  private readonly rowList = signal<TaskSummaryDto[]>([]);
  private readonly total = signal(0);
  private readonly loadState = signal<TaskListState>('loading');
  private readonly currentProject = signal<TaskProjectDto | null>(null);
  private readonly memberList = signal<TaskMemberDto[]>([]);

  readonly rows = this.rowList.asReadonly();
  readonly totalCount = this.total.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly project = this.currentProject.asReadonly();
  readonly members = this.memberList.asReadonly();

  /** Creating needs tasks.create and a real membership, in an open project (docs/SPEC.md §4.4). */
  readonly canCreate = computed(() => {
    const project = this.currentProject();
    return (
      project !== null &&
      project.myRole !== null &&
      project.archivedAt === null &&
      this.auth.has(PERMISSIONS.tasksCreate)
    );
  });
  readonly canSeeArchived = computed(() => this.auth.has(PERMISSIONS.tasksArchive));

  /** Only the last page asked for lands, however the answers arrive. */
  async load(request: TaskListRequest): Promise<void> {
    const asked = ++this.latest;
    this.loadState.set('loading');
    const { filters } = request;
    try {
      const page = await firstValueFrom(
        this.api.list(request.projectId, {
          page: request.page,
          pageSize: request.pageSize,
          sort: request.sort,
          assigneeId: filters.assignee === 'me' ? (this.auth.user()?.id ?? null) : filters.assignee,
          priorities: filters.priorities,
          overdue: filters.overdue,
          search: filters.search,
          includeArchived: filters.archived && this.canSeeArchived(),
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

  async loadProject(projectId: string): Promise<void> {
    if (this.currentProject()?.id === projectId) {
      return;
    }
    try {
      const [project, members] = await Promise.all([
        firstValueFrom(this.api.project(projectId)),
        firstValueFrom(this.api.members(projectId)),
      ]);
      this.currentProject.set(project);
      this.memberList.set(members);
    } catch {
      // The project page says itself when the project can't be read.
    }
  }

  create(task: NewTask): Promise<TaskItemDto> {
    const project = this.currentProject();
    if (!project) {
      return Promise.reject(new Error('No project to create the task in.'));
    }
    return firstValueFrom(this.api.create(project.id, task));
  }
}
