import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { BoardDto, BoardMemberDto, CreatedTaskDto } from './models';

/**
 * The board of a project (docs/UI_UX_SPEC.md §7.4, docs/SPEC.md §6.3). Moving a card needs a real
 * membership, creating one tasks.create too; the API checks them on every request.
 */
@Injectable({ providedIn: 'root' })
export class BoardApi {
  private readonly api = inject(ApiClient);

  board(projectId: string): Observable<BoardDto> {
    return this.api.get<BoardDto>(`/projects/${projectId}/board`);
  }

  members(projectId: string): Observable<BoardMemberDto[]> {
    return this.api.get<BoardMemberDto[]>(`/projects/${projectId}/members`);
  }

  /** To a place from 0 in the column; the API moves the others up or down. */
  move(taskId: string, columnId: string, position: number): Observable<unknown> {
    return this.api.patch<unknown>(`/tasks/${taskId}/position`, { columnId, position });
  }

  /** At the end of the column, with the default priority. */
  create(projectId: string, columnId: string, title: string): Observable<CreatedTaskDto> {
    return this.api.post<CreatedTaskDto>(`/projects/${projectId}/tasks`, {
      title,
      description: null,
      priority: 'Medium',
      columnId,
    });
  }
}
