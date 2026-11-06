import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Signal,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MAT_DATE_LOCALE, MatOption, provideNativeDateAdapter } from '@angular/material/core';
import {
  MatDatepicker,
  MatDatepickerInput,
  MatDatepickerToggle,
} from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogRef } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatSelect } from '@angular/material/select';
import { MatTab, MatTabGroup } from '@angular/material/tabs';
import { MatTooltip } from '@angular/material/tooltip';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { Avatar } from '../../shared/ui/avatar';
import { Banner } from '../../shared/ui/banner';
import { Chip } from '../../shared/ui/chip';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { Markdown } from '../../shared/ui/markdown';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { PRIORITIES, TaskPriority, priorityLabel } from './models';
import {
  TASK_TITLE_MAX,
  endOfDay,
  refusedFieldMessage,
  taskDescriptionProblem,
  taskTitleProblem,
} from './task-fields';
import { ChangeOutcome, TaskPanelStore } from './task-panel-store';

export interface TaskPanelData {
  /** The key in the address, which can change while the panel is open. */
  key: Signal<string>;
}

/**
 * Not a sheet but a panel (docs/UI_UX_SPEC.md §6.0.5): from the right, the height of the window less
 * 12px on each side, 920px wide, with the page still there behind the backdrop.
 */
export function taskPanelConfig(data: TaskPanelData): MatDialogConfig<TaskPanelData> {
  return {
    data,
    width: '920px',
    maxWidth: 'calc(100vw - 24px)',
    height: 'calc(100vh - 24px)',
    position: { top: '12px', right: '12px' },
    panelClass: 'kb-task-panel',
    ariaLabelledBy: 'kb-task-title',
    autoFocus: 'dialog',
  };
}

/**
 * A task's details (docs/UI_UX_SPEC.md §7.5). Left, the title, edited in place (Enter saves, Esc
 * goes back), and the description, written in basic markdown and rendered through kb-markdown.
 * Right, the properties, each changed where it is. The menu at the bottom right archives it, or
 * restores it for tasks.archive. Whoever can't change it reads the same panel without the controls.
 */
@Component({
  selector: 'kb-task-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    TaskPanelStore,
    provideNativeDateAdapter(),
    { provide: MAT_DATE_LOCALE, useValue: 'ro-RO' },
  ],
  imports: [
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatSelect,
    MatOption,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    MatTabGroup,
    MatTab,
    MatTooltip,
    RelativeTimePipe,
    Avatar,
    Banner,
    Chip,
    EmptyState,
    Markdown,
    Skeleton,
  ],
  templateUrl: './task-panel.html',
  styleUrl: './task-panel.scss',
})
export class TaskPanel {
  protected readonly store = inject(TaskPanelStore);
  private readonly data = inject<TaskPanelData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TaskPanel>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);

  protected readonly priorities = PRIORITIES;
  protected readonly priorityLabel = priorityLabel;
  protected readonly fullDate = fullDate;
  protected readonly titleMax = TASK_TITLE_MAX;

  protected readonly missingTitle = $localize`Task-ul nu există`;
  protected readonly forbiddenTitle = $localize`Nu ai acces la acest task`;
  protected readonly loadFailed = $localize`Nu am putut încărca task-ul.`;
  protected readonly unassigned = $localize`Neasignat`;
  /** Not null: mat-select takes an option of null for no choice at all, and would show nothing. */
  protected readonly unassignedValue = '';

  protected readonly editingTitle = signal(false);
  protected readonly titleDraft = signal('');
  protected readonly titleError = signal<string | null>(null);
  protected readonly editingDescription = signal(false);
  protected readonly descriptionDraft = signal('');
  protected readonly descriptionError = signal<string | null>(null);

  /** The deadline as the day it falls on, for the date picker. */
  protected readonly dueDay = computed(() => {
    const due = this.store.task()?.dueDate;
    return due ? new Date(due) : null;
  });

  private readonly titleInput = viewChild<ElementRef<HTMLInputElement>>('titleInput');

  private readonly unsaved = computed(() => {
    const task = this.store.task();
    return (
      (this.editingTitle() && this.titleDraft().trim() !== (task?.title ?? '')) ||
      (this.editingDescription() && this.descriptionDraft() !== (task?.description ?? ''))
    );
  });

  constructor() {
    askBeforeClosing(this.ref, () => this.unsaved(), this.confirmation);
    // The title's field takes the focus as it appears, with its text chosen, ready to be typed over.
    effect(() => {
      const input = this.titleInput()?.nativeElement;
      if (input) {
        input.focus();
        input.select();
      }
    });
    // Only the address starts a load: what load reads of the store mustn't start another one.
    effect(() => {
      const key = this.data.key();
      untracked(() => {
        this.editingTitle.set(false);
        this.editingDescription.set(false);
        void this.store.load(key);
      });
    });
  }

  protected retry(): void {
    void this.store.load(this.data.key());
  }

  protected editTitle(): void {
    this.titleDraft.set(this.store.task()?.title ?? '');
    this.titleError.set(null);
    this.editingTitle.set(true);
  }

  /** Esc goes back to the title as it was, and doesn't close the panel. */
  protected cancelTitle(event: Event): void {
    event.stopPropagation();
    this.editingTitle.set(false);
    this.titleError.set(null);
  }

  /** On Enter, or when the field is left. */
  protected async saveTitle(): Promise<void> {
    const task = this.store.task();
    if (!this.editingTitle() || !task) {
      return;
    }
    const title = this.titleDraft().trim();
    if (title === task.title) {
      this.editingTitle.set(false);
      return;
    }
    const problem = taskTitleProblem(title);
    if (problem) {
      this.titleError.set(problem);
      return;
    }

    this.editingTitle.set(false);
    const outcome = await this.store.rename(title);
    if (!outcome.saved && outcome.refusedField) {
      this.titleDraft.set(title);
      this.titleError.set(refusedFieldMessage(outcome.refusedField));
      this.editingTitle.set(true);
    }
  }

  protected editDescription(): void {
    this.descriptionDraft.set(this.store.task()?.description ?? '');
    this.descriptionError.set(null);
    this.editingDescription.set(true);
  }

  protected cancelDescription(): void {
    this.editingDescription.set(false);
    this.descriptionError.set(null);
  }

  protected async saveDescription(): Promise<void> {
    const problem = taskDescriptionProblem(this.descriptionDraft());
    if (problem) {
      this.descriptionError.set(problem);
      return;
    }
    const outcome = await this.store.describe(this.descriptionDraft());
    if (outcome.saved) {
      this.editingDescription.set(false);
    } else if (outcome.refusedField) {
      this.descriptionError.set(refusedFieldMessage(outcome.refusedField));
    }
  }

  protected async move(select: MatSelect, columnId: string): Promise<void> {
    this.revertIfRefused(select, this.store.task()?.columnId, await this.store.move(columnId));
  }

  protected async assign(select: MatSelect, value: string): Promise<void> {
    const before = this.store.task()?.assignee?.userId ?? this.unassignedValue;
    const assigneeId = value === this.unassignedValue ? null : value;
    this.revertIfRefused(select, before, await this.store.assign(assigneeId));
  }

  protected async prioritize(select: MatSelect, priority: TaskPriority): Promise<void> {
    this.revertIfRefused(
      select,
      this.store.task()?.priority,
      await this.store.prioritize(priority),
    );
  }

  protected async schedule(day: Date | null): Promise<void> {
    await this.store.schedule(day ? endOfDay(day) : null);
  }

  protected async archive(): Promise<void> {
    const task = this.store.task();
    if (!task) {
      return;
    }
    const confirmed = await this.confirmation.ask({
      title: $localize`Arhivezi task-ul «${task.title}:title:»?`,
      message: $localize`Nu mai apare pe board și în listă, dar istoricul lui rămâne. Cine are permisiunea de arhivare îl poate restaura.`,
      confirmLabel: $localize`Arhivează`,
      icon: 'archive',
      destructive: true,
    });
    if (confirmed && (await this.store.archive())) {
      this.snackbar.show($localize`Task-ul ${task.key}:key: a fost arhivat.`);
      this.ref.close();
    }
  }

  protected async restore(): Promise<void> {
    const outcome = await this.store.restore();
    if (outcome.saved) {
      this.snackbar.show($localize`Task-ul ${this.store.task()?.key ?? ''}:key: a fost restaurat.`);
    }
  }

  /**
   * A select shows what was chosen at once, so a change the API refused goes back to the value
   * the task kept. A field it refused gets its words; anything else the snackbar has shown.
   */
  private revertIfRefused(select: MatSelect, before: unknown, outcome: ChangeOutcome): void {
    if (outcome.saved) {
      return;
    }
    select.value = before;
    if (outcome.refusedField) {
      this.snackbar.error(refusedFieldMessage(outcome.refusedField), null);
    }
  }
}
