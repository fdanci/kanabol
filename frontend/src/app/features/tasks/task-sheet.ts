import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButton } from '@angular/material/button';
import { MAT_DATE_LOCALE, MatOption, provideNativeDateAdapter } from '@angular/material/core';
import {
  MatDatepicker,
  MatDatepickerInput,
  MatDatepickerToggle,
} from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSelect, MatSelectTrigger } from '@angular/material/select';
import { problemIn } from '../../shared/models/problem-details';
import { Avatar } from '../../shared/ui/avatar';
import { Chip } from '../../shared/ui/chip';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { Field } from '../../shared/ui/field';
import { Snackbar } from '../../shared/ui/snackbar';
import { PRIORITIES, TaskItemDto, TaskPriority, priorityLabel } from './models';
import {
  TASK_DESCRIPTION_MAX,
  TASK_TITLE_MAX,
  endOfDay,
  refusedFieldMessage,
  taskDescriptionProblem,
  taskTitleProblem,
} from './task-fields';
import { TaskListStore } from './task-list-store';

/**
 * A new task, in a 600px sheet with the labels on the left (docs/UI_UX_SPEC.md §6.0.4, §6.0.5): its
 * title, a description in basic markdown, the priority, Backlog, where it starts, who it's for and a
 * deadline. It closes with the task as the API saved it, its key among the rest.
 */
@Component({
  selector: 'kb-task-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideNativeDateAdapter(), { provide: MAT_DATE_LOCALE, useValue: 'ro-RO' }],
  imports: [
    MatButton,
    MatSelect,
    MatSelectTrigger,
    MatOption,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    Avatar,
    Chip,
  ],
  templateUrl: './task-sheet.html',
  styleUrl: './task-sheet.scss',
})
export class TaskSheet {
  protected readonly store = inject<TaskListStore>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TaskSheet, TaskItemDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);

  protected readonly titleMax = TASK_TITLE_MAX;
  protected readonly descriptionMax = TASK_DESCRIPTION_MAX;
  protected readonly priorities = PRIORITIES;
  protected readonly priorityLabel = priorityLabel;
  protected readonly unassigned = $localize`Neasignat`;
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  protected readonly title = new Field('', taskTitleProblem, this.submitted);
  protected readonly description = new Field('', taskDescriptionProblem, this.submitted);
  protected readonly priority = signal<TaskPriority>('Medium');
  /** Any number of the active members (docs/UI_UX_SPEC.md §7.3), sent with the task. */
  protected readonly assigneeIds = signal<string[]>([]);
  protected readonly dueDay = signal<Date | null>(null);

  protected readonly assignees = computed(() =>
    this.store.members().filter((member) => member.isActive),
  );
  /** Who the ticks are, by name, as the select shows them. */
  protected readonly chosenPeople = computed(() =>
    this.assignees()
      .filter((member) => this.assigneeIds().includes(member.userId))
      .sort((a, b) => a.displayName.localeCompare(b.displayName, 'ro-RO', { sensitivity: 'base' })),
  );

  private readonly changed = computed(
    () =>
      this.title.changed() ||
      this.description.changed() ||
      this.priority() !== 'Medium' ||
      this.assigneeIds().length > 0 ||
      this.dueDay() !== null,
  );

  constructor() {
    askBeforeClosing(this.ref, () => this.changed(), this.confirmation);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.title.error() !== null || this.description.error() !== null) {
      return;
    }

    this.busy.set(true);
    try {
      const description = this.description.value();
      const day = this.dueDay();
      const saved = await this.store.create({
        title: this.title.value().trim(),
        description: description.trim() === '' ? null : description,
        priority: this.priority(),
        assigneeIds: this.assigneeIds(),
        dueDate: day ? endOfDay(day) : null,
      });
      this.ref.close(saved);
    } catch (error) {
      if (!this.refused(error)) {
        this.snackbar.error($localize`Task-ul nu a fost creat. Reîncearcă.`, null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }

  /** Puts what the API refused under its field, or in the snackbar for a choice. False if nothing explains it. */
  private refused(error: unknown): boolean {
    const problem = error instanceof HttpErrorResponse ? problemIn(error) : null;
    if (!problem?.errors) {
      // Not about a field: errorInterceptor has shown it.
      return true;
    }
    for (const field of Object.keys(problem.errors)) {
      if (field === 'title') {
        this.title.refused.set(refusedFieldMessage(field));
      } else if (field === 'description') {
        this.description.refused.set(refusedFieldMessage(field));
      } else {
        this.snackbar.error(refusedFieldMessage(field), null);
      }
    }
    return true;
  }
}
