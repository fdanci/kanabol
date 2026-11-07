import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import { TaskSort, sortToQuery } from '../../shared/models/task-filters';
import {
  NewTask,
  TaskItemDto,
  TaskMemberDto,
  TaskPriority,
  TaskProjectDto,
  TaskSummaryDto,
} from './models';

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
