import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Chip, ChipKind } from './chip';

const THREE_DAYS = 3 * 24 * 60 * 60 * 1000;

/**
 * The deadline chip of docs/UI_UX_SPEC.md §6.5: green with more than three days left, warning within
 * three, danger once past. A finished task has nothing left to be late for, so its chip is neutral.
 */
export function deadlineKind(dueDate: string, done: boolean, now: number = Date.now()): ChipKind {
  if (done) {
    return 'neutral';
  }
  const left = new Date(dueDate).getTime() - now;
  if (left < 0) {
    return 'deadline-overdue';
  }
  return left <= THREE_DAYS ? 'deadline-soon' : 'deadline-ok';
}

/** dd.MM.yyyy, where the person is (docs/UI_UX_SPEC.md §11). */
export function formatDay(iso: string): string {
  return new Date(iso).toLocaleDateString('ro-RO', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

/** A task's deadline, on the list, the board and the panel. */
@Component({
  selector: 'kb-deadline-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Chip],
  template: `<kb-chip [kind]="kind()">{{ day() }}</kb-chip>`,
})
export class DeadlineChip {
  readonly dueDate = input.required<string>();
  readonly done = input(false);

  protected readonly kind = computed(() => deadlineKind(this.dueDate(), this.done()));
  protected readonly day = computed(() => formatDay(this.dueDate()));
}
