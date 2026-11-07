import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskChanges } from '../../shared/task-changes';
import { CommentDto } from './models';
import { CommentsApi } from './comments-api';

export type TaskCommentsState = 'loading' | 'ready' | 'error';

/**
 * A task's comments, for its panel (docs/UI_UX_SPEC.md §7.5): all of them, from the oldest, a new one
 * at the end, an edited one in its place. A comment counts on the board's card, so adding one tells
 * TaskChanges, which makes the page behind read itself again. A refusal keeps the list as it was, and
 * the snackbar says why.
 */
@Injectable()
export class TaskCommentsStore {
  private readonly api = inject(CommentsApi);
  private readonly changes = inject(TaskChanges);
  private readonly list = signal<CommentDto[]>([]);
  private readonly loadState = signal<TaskCommentsState>('loading');
  private latest = 0;
  private taskId = '';

  readonly comments = this.list.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly saving = signal(false);

  /** Only the last task asked for lands, however the answers arrive. */
  async load(taskId: string): Promise<void> {
    const request = ++this.latest;
    this.taskId = taskId;
    this.list.set([]);
    this.loadState.set('loading');
    try {
      const comments = await firstValueFrom(this.api.list(taskId));
      if (request === this.latest) {
        this.list.set(comments);
        this.loadState.set('ready');
      }
    } catch {
      if (request === this.latest) {
        this.loadState.set('error');
      }
    }
  }

  /** Whether the API took it. */
  async add(body: string): Promise<boolean> {
    return this.save(async () => {
      const added = await firstValueFrom(this.api.add(this.taskId, body));
      this.list.update((comments) => [...comments, added]);
      this.changes.saved();
    });
  }

  async edit(id: string, body: string): Promise<boolean> {
    return this.save(async () => {
      const edited = await firstValueFrom(this.api.edit(id, body));
      this.list.update((comments) =>
        comments.map((comment) => (comment.id === id ? edited : comment)),
      );
    });
  }

  private async save(send: () => Promise<void>): Promise<boolean> {
    this.saving.set(true);
    try {
      await send();
      return true;
    } catch {
      return false;
    } finally {
      this.saving.set(false);
    }
  }
}
