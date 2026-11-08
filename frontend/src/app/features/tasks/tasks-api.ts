import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import { TaskSort, sortToQuery } from '../../shared/models/task-filters';
import {
  MyTaskDto,
  NewTask,
  TaskHistoryEntryDto,
  TaskItemDto,
  TaskMemberDto,
  TaskPriority,
  TaskProjectDto,
  TaskSummaryDto,
} from './models';

/** The timeline reads a task's history 50 changes at a time (docs/UI_UX_SPEC.md §7.5). */
export const HISTORY_PAGE_SIZE = 50;

/** "Task-urile mele" go by deadline from the soonest, those without one last (docs/UI_UX_SPEC.md §7.2). */
export const MY_TASKS_SORT: TaskSort = { field: 'dueDate', descending: false };

/** A page of the user's own tasks: the List's filters but the assignee, and only the open ones with open. */
export interface MyTasksQuery {
  page: number;
  pageSize: number;
  sort: TaskSort;
  open: boolean;
  priorities: TaskPriority[];
  overdue: boolean;
  search: string;
  includeArchived: boolean;
}

/** A page of a project's tasks: the board's filters, with the assignee as an id. */
export interface TaskListQuery {
  page: number;
  pageSize: number;
  sort: TaskSort;
  assigneeId: string | null;
  priorities: TaskPriority[];
  overdue: boolean;
  search: string;
  includeArchived: boolean;
}

/**
 * Tasks (docs/SPEC.md §4.4, §5.1). Creating one needs tasks.create and a real membership, changing one
 * a real membership, and archived ones need tasks.archive; the API checks all of it on every request.
 */
@Injectable({ providedIn: 'root' })
export class TasksApi {
  private readonly api = inject(ApiClient);

  /** The panel says itself that a task is gone or closed to the caller. */
  byKey(key: string, includeArchived: boolean): Observable<TaskItemDto> {
    return this.api.get<TaskItemDto>(`/tasks/by-key/${encodeURIComponent(key)}`, {
      params: includeArchived ? { includeArchived } : {},
      context: handledHere('Task.NotFound', 'Project.MemberRequired'),
    });
  }

  list(projectId: string, query: TaskListQuery): Observable<Page<TaskSummaryDto>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const sort = sortToQuery(query.sort);
    if (sort) {
      params = params.set('sort', sort);
    }
    if (query.assigneeId) {
      params = params.set('assigneeId', query.assigneeId);
    }
    for (const priority of query.priorities) {
      params = params.append('priority', priority);
    }
    if (query.overdue) {
      params = params.set('overdue', true);
    }
    if (query.search.trim() !== '') {
      params = params.set('search', query.search.trim());
    }
    if (query.includeArchived) {
      params = params.set('includeArchived', true);
    }
    return this.api.get<Page<TaskSummaryDto>>(`/projects/${projectId}/tasks`, { params });
  }

  /** A page of the task's changes, from the newest; an archived task's only for tasks.archive. */
  history(id: string, page: number): Observable<Page<TaskHistoryEntryDto>> {
    return this.api.get<Page<TaskHistoryEntryDto>>(`/tasks/${id}/history`, {
      params: { page, pageSize: HISTORY_PAGE_SIZE },
    });
  }

  /**
   * One change of the task's history, whole: a description's texts, which the list leaves out, for its
   * diff (docs/SPEC.md §4.4). Another task's change is 404 History.NotFound, which the diff's block
   * says itself; the snackbar has the rest.
   */
  historyEntry(id: string, entryId: string): Observable<TaskHistoryEntryDto> {
    return this.api.get<TaskHistoryEntryDto>(`/tasks/${id}/history/${entryId}`, {
      context: handledHere('History.NotFound'),
    });
  }

  /** The user's own tasks across every project (docs/SPEC.md §4.4), the API's default sort left out. */
  myTasks(query: MyTasksQuery): Observable<Page<MyTaskDto>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const sort = sortToQuery(query.sort, MY_TASKS_SORT);
    if (sort) {
      params = params.set('sort', sort);
    }
    if (query.open) {
      params = params.set('open', true);
    }
    for (const priority of query.priorities) {
      params = params.append('priority', priority);
    }
    if (query.overdue) {
      params = params.set('overdue', true);
    }
    if (query.search.trim() !== '') {
      params = params.set('search', query.search.trim());
    }
    if (query.includeArchived) {
      params = params.set('includeArchived', true);
    }
    return this.api.get<Page<MyTaskDto>>('/me/tasks', { params });
  }

  /** The project's columns and the caller's role in it, for the panel's choices. */
  project(projectId: string): Observable<TaskProjectDto> {
    return this.api.get<TaskProjectDto>(`/projects/${projectId}`);
  }

  members(projectId: string): Observable<TaskMemberDto[]> {
    return this.api.get<TaskMemberDto[]>(`/projects/${projectId}/members`);
  }

  create(projectId: string, task: NewTask): Observable<TaskItemDto> {
    return this.api.post<TaskItemDto>(`/projects/${projectId}/tasks`, task);
  }

  rename(id: string, title: string): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/title`, { title });
  }

  describe(id: string, description: string | null): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/description`, { description });
  }

  /** The whole list, which replaces the one before (docs/SPEC.md §4.4): an empty one leaves it to nobody. */
  assign(id: string, assigneeIds: string[]): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/assignees`, { assigneeIds });
  }

  prioritize(id: string, priority: TaskPriority): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/priority`, { priority });
  }

  schedule(id: string, dueDate: string | null): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/due-date`, { dueDate });
  }

  /** Without a position the task goes last in the column. */
  move(id: string, columnId: string, position?: number): Observable<TaskItemDto> {
    return this.api.patch<TaskItemDto>(`/tasks/${id}/position`, { columnId, position });
  }

  /** DELETE archives: nothing deletes a task (docs/SPEC.md §5.1). */
  archive(id: string): Observable<void> {
    return this.api.delete(`/tasks/${id}`);
  }

  restore(id: string): Observable<TaskItemDto> {
    return this.api.post<TaskItemDto>(`/tasks/${id}/restore`);
  }
}
