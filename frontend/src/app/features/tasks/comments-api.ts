import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { CommentDto } from './models';

/**
 * A task's comments (docs/SPEC.md §4.4): read by whoever sees the project, written by a real member of an
 * open project on an open task, edited only by their author. Nothing deletes one.
 */
@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly api = inject(ApiClient);

  /** All of them, from the oldest, without pages. */
  list(taskId: string): Observable<CommentDto[]> {
    return this.api.get<CommentDto[]>(`/tasks/${taskId}/comments`);
  }

  add(taskId: string, body: string): Observable<CommentDto> {
    return this.api.post<CommentDto>(`/tasks/${taskId}/comments`, { body });
  }

  edit(id: string, body: string): Observable<CommentDto> {
    return this.api.put<CommentDto>(`/comments/${id}`, { body });
  }
}
