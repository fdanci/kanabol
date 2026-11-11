import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { BoardColumnDto, BoardDto, BoardMemberDto, CreatedTaskDto, MoveAnswers } from './models';

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

  /**
   * To a place from 0 in the column; the API moves the others up or down. Into În lucru without an estimate,
   * or into În pauză, with what the move asked for (docs/SPEC.md §4.4).
   */
  move(
    taskId: string,
    columnId: string,
    position: number,
    answers: MoveAnswers = {},
  ): Observable<unknown> {
    return this.api.patch<unknown>(`/tasks/${taskId}/position`, { columnId, position, ...answers });
  }

  /** A column of the project's own. The columns change for owners and projects.manage only. */
  addColumn(projectId: string, name: string): Observable<BoardColumnDto> {
    return this.api.post<BoardColumnDto>(`/projects/${projectId}/board-columns`, { name });
  }

  /** Another name; the kind never changes (docs/SPEC.md §4.4). */
  updateColumn(projectId: string, columnId: string, name: string): Observable<BoardColumnDto> {
    return this.api.put<BoardColumnDto>(`/projects/${projectId}/board-columns/${columnId}`, {
      name,
    });
  }

  /** Answers every column in its new order. */
  moveColumn(projectId: string, columnId: string, position: number): Observable<BoardColumnDto[]> {
    return this.api.patch<BoardColumnDto[]>(
      `/projects/${projectId}/board-columns/${columnId}/position`,
      { position },
    );
  }

  /** Only an empty one, archived tasks counting, and never the last: 409 otherwise. */
  deleteColumn(projectId: string, columnId: string): Observable<void> {
    return this.api.delete(`/projects/${projectId}/board-columns/${columnId}`);
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
