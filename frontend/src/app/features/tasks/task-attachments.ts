import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { Subscription, firstValueFrom } from 'rxjs';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { UploadItem, UploadZone } from '../../shared/ui/upload-zone';
import { AttachmentsApi } from './attachments-api';
import { AttachmentDto, TaskItemDto } from './models';
import { ATTACHMENT_MAX_BYTES, refusedFieldMessage } from './task-fields';

const SIZE = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 1 });

/** Bytes as people read them, in ro-RO: "1,5 KB", "25 MB". */
export function fileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${SIZE.format(bytes)} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${SIZE.format(bytes / 1024)} KB`;
  }
  return `${SIZE.format(bytes / (1024 * 1024))} MB`;
}

/** A link the page clicks for the person: the file came with the token, which a plain link can't carry. */
function save(file: Blob, fileName: string): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

/**
 * The "Atașamente" tab of a task's panel (docs/UI_UX_SPEC.md §6.3, §7.5): the files, each downloaded
 * under its name; for whoever can change the task, kb-upload-zone, every file chosen going up in a
 * request of its own (docs/SPEC.md §10), with a row and its progress until the API has it. A file over
 * 25 MB is refused in its row without being sent; one the API refuses says so in its row, until it's
 * taken away. Taking away a row going up stops it. The panel keeps the list: this tells it what came
 * and what went.
 */
@Component({
  selector: 'kb-task-attachments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, MatTooltip, RelativeTimePipe, UploadZone],
  template: `
    @if (task().attachments.length === 0) {
      <p class="empty" i18n>Niciun atașament încă.</p>
    } @else {
      <ul class="kb-form-group attachments">
        @for (attachment of task().attachments; track attachment.id) {
          <li class="attachment">
            <span class="material-symbols-outlined icon" aria-hidden="true">attach_file</span>
            <button
              type="button"
              class="name kb-truncate"
              [attr.aria-label]="downloadLabel(attachment.fileName)"
              (click)="download(attachment)"
            >
              {{ attachment.fileName }}
            </button>
            <span class="size">{{ size(attachment.size) }}</span>
            <span class="by"
              >{{ attachment.uploadedBy.displayName }} ·
              <time
                [attr.datetime]="attachment.uploadedAt"
                [matTooltip]="fullDate(attachment.uploadedAt)"
                >{{ attachment.uploadedAt | relativeTime }}</time
              ></span
            >
            @if (attachment.canDelete) {
              <button
                matIconButton
                type="button"
                class="delete"
                [attr.aria-label]="deleteLabel(attachment.fileName)"
                (click)="remove(attachment)"
              >
                <span class="material-symbols-outlined" aria-hidden="true">delete</span>
              </button>
            }
          </li>
        }
      </ul>
    }

    @if (task().canChange) {
      <kb-upload-zone [uploads]="uploads()" (chosen)="send($event)" (removed)="dismiss($event)" />
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-4);
      padding: var(--kb-space-4) 0;
    }

    .attachments {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .attachment {
      display: flex;
      align-items: center;
      gap: var(--kb-space-3);
      min-height: 36px;
      padding: 0 var(--kb-space-1) 0 var(--kb-space-3);
      font-size: 13px;
    }

    .attachment + .attachment {
      border-top: 1px solid var(--kb-border-200);
    }

    .icon {
      color: var(--kb-text-500);
      font-size: 16px;
    }

    .name {
      flex: 1;
      min-width: 0;
      padding: 0;
      border: 0;
      background: none;
      color: var(--kb-accent-600);
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    .name:hover {
      text-decoration: underline;
    }

    .size,
    .by {
      color: var(--kb-text-500);
      font-size: 12px;
      white-space: nowrap;
    }

    .delete .material-symbols-outlined {
      font-size: 16px;
    }

    .empty {
      margin: 0;
      color: var(--kb-text-500);
      font-size: 13px;
    }
  `,
})
export class TaskAttachments {
  readonly task = input.required<TaskItemDto>();
  /** A file the API now has, for the panel's list. */
  readonly attached = output<AttachmentDto>();
  /** The id of a file deleted. */
  readonly detached = output<string>();

  private readonly api = inject(AttachmentsApi);
  private readonly confirmation = inject(Confirmation);
  private readonly going = new Map<string, Subscription>();
  private next = 0;

  protected readonly uploads = signal<UploadItem[]>([]);
  protected readonly size = fileSize;
  protected readonly fullDate = fullDate;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      for (const upload of this.going.values()) {
        upload.unsubscribe();
      }
    });
  }

  protected downloadLabel(fileName: string): string {
    return $localize`Descarcă ${fileName}:name:`;
  }

  protected deleteLabel(fileName: string): string {
    return $localize`Șterge ${fileName}:name:`;
  }

  protected send(files: File[]): void {
    const taskId = this.task().id;
    for (const file of files) {
      const id = `upload-${String(++this.next)}`;
      if (file.size > ATTACHMENT_MAX_BYTES) {
        this.uploads.update((items) => [
          ...items,
          { id, name: file.name, progress: null, error: $localize`Trece de 25 MB.` },
        ]);
        continue;
      }
      this.uploads.update((items) => [...items, { id, name: file.name, progress: 0 }]);
      this.going.set(
        id,
        this.api.upload(taskId, file).subscribe({
          next: ({ progress, attachment }) => {
            if (attachment) {
              this.going.delete(id);
              this.attached.emit(attachment);
              this.forget(id);
            } else {
              this.change(id, { progress });
            }
          },
          error: (error: unknown) => {
            this.going.delete(id);
            this.change(id, { progress: null, error: refusal(error) });
          },
        }),
      );
    }
  }

  /** A row taken away: a file still going up stops. */
  protected dismiss(id: string): void {
    this.going.get(id)?.unsubscribe();
    this.going.delete(id);
    this.forget(id);
  }

  protected async download(attachment: AttachmentDto): Promise<void> {
    try {
      save(await firstValueFrom(this.api.download(attachment.id)), attachment.fileName);
    } catch {
      // The snackbar said why.
    }
  }

  protected async remove(attachment: AttachmentDto): Promise<void> {
    const confirmed = await this.confirmation.ask({
      title: $localize`Ștergi fișierul «${attachment.fileName}:name:»?`,
      message: $localize`Fișierul se șterge și de pe server și nu poate fi recuperat.`,
      confirmLabel: $localize`Șterge`,
      icon: 'delete',
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    try {
      await firstValueFrom(this.api.delete(attachment.id));
      this.detached.emit(attachment.id);
    } catch {
      // The snackbar said why, and the file stays.
    }
  }

  private change(id: string, changes: Partial<UploadItem>): void {
    this.uploads.update((items) =>
      items.map((item) => (item.id === id ? { ...item, ...changes } : item)),
    );
  }

  private forget(id: string): void {
    this.uploads.update((items) => items.filter((item) => item.id !== id));
  }
}

/** A file the API refused on its field gets the panel's words; anything else, which the snackbar showed, a short one. */
function refusal(error: unknown): string {
  return error instanceof HttpErrorResponse && error.status === 400
    ? refusedFieldMessage('file')
    : $localize`Nu s-a putut atașa. Reîncearcă.`;
}
