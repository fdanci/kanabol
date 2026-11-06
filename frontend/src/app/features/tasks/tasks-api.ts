import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { NewTask, TaskItemDto, TaskMemberDto, TaskPriority, TaskProjectDto } from './models';

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

  assign(id: string, assigneeId: string | null): Observable<TaskItemDto> {
    return this.api.put<TaskItemDto>(`/tasks/${id}/assignee`, { assigneeId });
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
