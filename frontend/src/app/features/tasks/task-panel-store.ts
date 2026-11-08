import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { problemIn } from '../../shared/models/problem-details';
import { AttachmentDto, TaskColumnDto, TaskItemDto, TaskMemberDto, TaskPriority } from './models';
import { TaskChanges } from '../../shared/task-changes';
import { TasksApi } from './tasks-api';

export type TaskPanelState = 'loading' | 'ready' | 'missing' | 'forbidden' | 'error';

/** What a change came to: saved, refused on a field, which the panel explains in its words, or failed. */
export type ChangeOutcome = { saved: true } | { saved: false; refusedField: string | null };

/**
 * One task, for its panel (docs/UI_UX_SPEC.md §7.5): read by its key, with the choices its project
 * offers, and changed one property at a time. The API answers each change with the task as saved.
 * A change made by someone else meanwhile (409) reads the task again, and the snackbar says why.
 */
@Injectable()
export class TaskPanelStore {
  private readonly api = inject(TasksApi);
  private readonly auth = inject(AuthStore);
  private readonly changes = inject(TaskChanges);
  private latest = 0;
  private key = '';

  private readonly current = signal<TaskItemDto | null>(null);
  private readonly loadState = signal<TaskPanelState>('loading');
  private readonly columnList = signal<TaskColumnDto[]>([]);
  private readonly memberList = signal<TaskMemberDto[]>([]);

  readonly task = this.current.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly columns = this.columnList.asReadonly();
  readonly saving = signal(false);
  /** How many changes the panel saved, so its timeline reads the history again after each one. */
  readonly savedChanges = signal(0);
  readonly canChange = computed(() => this.current()?.canChange ?? false);
  readonly canRestore = computed(
    () => this.current()?.archivedAt != null && this.auth.has(PERMISSIONS.tasksArchive),
  );

  /**
   * Who can be given the task: the active members, and whoever has it now, even deactivated, who stays
   * until someone takes them off (docs/SPEC.md §4.4).
   */
  readonly assignees = computed(() => {
    const active = this.memberList()
      .filter((member) => member.isActive)
      .map((member) => ({
        userId: member.userId,
        displayName: member.displayName,
        isActive: true,
      }));
    const kept = (this.current()?.assignees ?? [])
      .filter((person) => !active.some((member) => member.userId === person.userId))
      .map((person) => ({
        userId: person.userId,
        displayName: person.displayName,
        isActive: false,
      }));
    return [...active, ...kept];
  });

  /** Only the last task asked for lands, however the answers arrive. */
  async load(key: string): Promise<void> {
    const request = ++this.latest;
    this.key = key;
    if (this.current()?.key !== key) {
      this.current.set(null);
      this.loadState.set('loading');
    }

    try {
      // Whoever may restore a task can open an archived one too.
      const task = await firstValueFrom(
        this.api.byKey(key, this.auth.has(PERMISSIONS.tasksArchive)),
      );
      if (request !== this.latest) {
        return;
      }
      this.current.set(task);
      this.loadState.set('ready');
      if (task.canChange) {
        await this.loadChoices(task.projectId, request);
      }
    } catch (error) {
      if (request === this.latest) {
        this.loadState.set(stateFor(error));
      }
    }
  }

  rename(title: string): Promise<ChangeOutcome> {
    return this.change((id) => this.api.rename(id, title));
  }

  describe(description: string): Promise<ChangeOutcome> {
    return this.change((id) =>
      this.api.describe(id, description.trim() === '' ? null : description),
    );
  }

  /** The whole list, which replaces the one before. */
  assign(assigneeIds: string[]): Promise<ChangeOutcome> {
    return this.change((id) => this.api.assign(id, assigneeIds));
  }

  prioritize(priority: TaskPriority): Promise<ChangeOutcome> {
    return this.change((id) => this.api.prioritize(id, priority));
  }

  schedule(dueDate: string | null): Promise<ChangeOutcome> {
    return this.change((id) => this.api.schedule(id, dueDate));
  }

  /** From the panel a task goes last in the column chosen. */
  move(columnId: string): Promise<ChangeOutcome> {
    return this.change((id) => this.api.move(id, columnId));
  }

  async archive(): Promise<boolean> {
    const task = this.current();
    if (!task) {
      return false;
    }
    try {
      await firstValueFrom(this.api.archive(task.id));
      this.changes.saved();
      return true;
    } catch {
      return false;
    }
  }

  restore(): Promise<ChangeOutcome> {
    return this.change((id) => this.api.restore(id));
  }

  /**
   * A file the API now has, at the end of the task's list. It counts on the board's card, so the page
   * behind reads itself again, as after any change.
   */
  attached(attachment: AttachmentDto): void {
    this.current.update((task) =>
      task ? { ...task, attachments: [...task.attachments, attachment] } : task,
    );
    this.changes.saved();
  }

  detached(attachmentId: string): void {
    this.current.update((task) =>
      task
        ? { ...task, attachments: task.attachments.filter((found) => found.id !== attachmentId) }
        : task,
    );
    this.changes.saved();
  }

  private async change(send: (id: string) => Observable<TaskItemDto>): Promise<ChangeOutcome> {
    const task = this.current();
    if (!task) {
      return { saved: false, refusedField: null };
    }

    this.saving.set(true);
    try {
      this.current.set(await firstValueFrom(send(task.id)));
      this.changes.saved();
      this.savedChanges.update((count) => count + 1);
      return { saved: true };
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        void this.load(this.key);
      } else if (error instanceof HttpErrorResponse && error.status === 404) {
        this.loadState.set('missing');
      }
      return { saved: false, refusedField: refusedField(error) };
    } finally {
      this.saving.set(false);
    }
  }

  private async loadChoices(projectId: string, request: number): Promise<void> {
    try {
      const [project, members] = await Promise.all([
        firstValueFrom(this.api.project(projectId)),
        firstValueFrom(this.api.members(projectId)),
      ]);
      if (request === this.latest) {
        this.columnList.set([...project.columns].sort((a, b) => a.position - b.position));
        this.memberList.set(members);
      }
    } catch {
      // The task still shows; only its choices are missing, and the snackbar said why.
    }
  }
}

function stateFor(error: unknown): TaskPanelState {
  if (error instanceof HttpErrorResponse && error.status === 404) {
    return 'missing';
  }
  if (error instanceof HttpErrorResponse && error.status === 403) {
    return 'forbidden';
  }
  return 'error';
}

/** The field a 400 is about. Its English words stay in the API: the panel has its own. */
function refusedField(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) {
    return null;
  }
  return Object.keys(problemIn(error)?.errors ?? {})[0] ?? null;
}
