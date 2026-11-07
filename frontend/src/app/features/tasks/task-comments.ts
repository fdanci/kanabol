import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { Avatar } from '../../shared/ui/avatar';
import { Markdown } from '../../shared/ui/markdown';
import { Skeleton } from '../../shared/ui/skeleton';
import { CommentDto } from './models';
import { commentProblem } from './task-fields';
import { TaskCommentsStore } from './task-comments-store';

/**
 * The "Comentarii" tab of a task's panel (docs/UI_UX_SPEC.md §7.5): each comment an avatar, a name, a
 * relative time with the full date in its tooltip, its text through kb-markdown; "Editează" only on the
 * caller's own, which turns it into a field in place. Whoever can comment has a form at the end, sent
 * with its button or Ctrl+Enter, Enter alone starting a new line.
 */
@Component({
  selector: 'kb-task-comments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TaskCommentsStore],
  imports: [MatButton, MatTooltip, RelativeTimePipe, Avatar, Markdown, Skeleton],
  template: `
    @switch (store.state()) {
      @case ('loading') {
        <div class="loading" aria-busy="true">
          <kb-skeleton width="40%" />
          <kb-skeleton width="80%" />
        </div>
      }
      @case ('error') {
        <p class="empty" i18n>Nu am putut încărca comentariile.</p>
        <button matButton class="kb-sm" type="button" (click)="reload()" i18n>Reîncearcă</button>
      }
      @default {
        @if (store.comments().length === 0) {
          <p class="empty" i18n>Niciun comentariu încă.</p>
        } @else {
          <ol class="comments">
            @for (comment of store.comments(); track comment.id) {
              <li class="comment">
                <kb-avatar [name]="comment.author.displayName" />
                <div class="content">
                  <p class="meta">
                    <span class="author">{{ comment.author.displayName }}</span>
                    <time
                      [attr.datetime]="comment.createdAt"
                      [matTooltip]="fullDate(comment.createdAt)"
                      >{{ comment.createdAt | relativeTime }}</time
                    >
                    @if (comment.updatedAt; as updated) {
                      <span class="edited" [matTooltip]="editedAt(updated)" i18n>editat</span>
                    }
                    @if (comment.canEdit && canComment() && editing() !== comment.id) {
                      <button
                        matButton
                        class="kb-sm edit"
                        type="button"
                        (click)="edit(comment)"
                        i18n
                      >
                        Editează
                      </button>
                    }
                  </p>
                  @if (editing() === comment.id) {
                    <textarea
                      class="kb-field"
                      rows="4"
                      i18n-aria-label
                      aria-label="Comentariul tău"
                      [value]="editDraft()"
                      [attr.aria-invalid]="editError() !== null"
                      (input)="editDraft.set($any($event.target).value)"
                      (keydown.control.enter)="saveEdit()"
                      (keydown.meta.enter)="saveEdit()"
                    ></textarea>
                    @if (editError(); as error) {
                      <p class="kb-field-error">{{ error }}</p>
                    }
                    <div class="actions">
                      <button
                        matButton="outlined"
                        class="kb-bezel kb-regular"
                        type="button"
                        (click)="cancelEdit()"
                        i18n
                      >
                        Anulează
                      </button>
                      <button
                        matButton="filled"
                        class="kb-default kb-regular"
                        type="button"
                        [disabled]="store.saving() || editDraft().trim() === ''"
                        (click)="saveEdit()"
                        i18n
                      >
                        Salvează
                      </button>
                    </div>
                  } @else {
                    <kb-markdown [text]="comment.body" />
                  }
                </div>
              </li>
            }
          </ol>
        }

        @if (canComment()) {
          <form class="new" (submit)="$event.preventDefault(); send()">
            <textarea
              class="kb-field"
              rows="3"
              i18n-placeholder
              placeholder="Scrie un comentariu"
              i18n-aria-label
              aria-label="Comentariu nou"
              aria-describedby="kb-comment-help"
              [value]="draft()"
              [attr.aria-invalid]="draftError() !== null"
              (input)="draft.set($any($event.target).value)"
              (keydown.control.enter)="send()"
              (keydown.meta.enter)="send()"
            ></textarea>
            @if (draftError(); as error) {
              <p class="kb-field-error">{{ error }}</p>
            }
            <div class="actions">
              <p id="kb-comment-help" class="help" i18n>Markdown de bază. Ctrl+Enter trimite.</p>
              <button
                matButton="filled"
                class="kb-default kb-regular"
                type="submit"
                [disabled]="!canSend()"
                i18n
              >
                Comentează
              </button>
            </div>
          </form>
        }
      }
    }
  `,
  styles: `
    :host {
      display: block;
      padding: var(--kb-space-4) 0;
    }

    .comments {
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .comment {
      display: flex;
      gap: var(--kb-space-3);
    }

    .content {
      flex: 1;
      min-width: 0;
    }

    .meta {
      display: flex;
      align-items: center;
      gap: var(--kb-space-2);
      min-height: 24px;
      margin: 0 0 var(--kb-space-1);
      color: var(--kb-text-500);
      font-size: 12px;
    }

    .author {
      color: var(--kb-text-900);
      font-size: 13px;
      font-weight: 600;
    }

    .edit {
      margin-left: auto;
    }

    textarea {
      width: 100%;
      resize: vertical;
    }

    .new {
      margin-top: var(--kb-space-4);
    }

    .actions {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      gap: var(--kb-space-2);
      margin-top: var(--kb-space-2);
    }

    .help {
      margin: 0 auto 0 0;
      color: var(--kb-text-500);
      font-size: 12px;
    }

    .empty {
      margin: 0;
      color: var(--kb-text-500);
      font-size: 13px;
    }

    .loading {
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
    }
  `,
})
export class TaskComments {
  readonly taskId = input.required<string>();
  /** A real member of an open project, the task open: the panel's canChange. */
  readonly canComment = input(false);

  protected readonly store = inject(TaskCommentsStore);
  protected readonly fullDate = fullDate;

  protected readonly draft = signal('');
  protected readonly draftError = signal<string | null>(null);
  protected readonly editing = signal<string | null>(null);
  protected readonly editDraft = signal('');
  protected readonly editError = signal<string | null>(null);
  protected readonly canSend = computed(() => this.draft().trim() !== '' && !this.store.saving());

  constructor() {
    effect(() => {
      const taskId = this.taskId();
      untracked(() => {
        this.editing.set(null);
        void this.store.load(taskId);
      });
    });
  }

  protected reload(): void {
    void this.store.load(this.taskId());
  }

  protected editedAt(updated: string): string {
    return $localize`Editat ${fullDate(updated)}:date:`;
  }

  /** The form keeps its text until the API took it. */
  protected async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const body = this.draft();
    const problem = commentProblem(body);
    this.draftError.set(problem);
    if (problem) {
      return;
    }
    if (await this.store.add(body)) {
      this.draft.set('');
    }
  }

  protected edit(comment: CommentDto): void {
    this.editDraft.set(comment.body);
    this.editError.set(null);
    this.editing.set(comment.id);
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.editError.set(null);
  }

  protected async saveEdit(): Promise<void> {
    const id = this.editing();
    const body = this.editDraft();
    if (id === null || body.trim() === '') {
      return;
    }
    const problem = commentProblem(body);
    this.editError.set(problem);
    if (problem) {
      return;
    }
    if (await this.store.edit(id, body)) {
      this.editing.set(null);
    }
  }
}
