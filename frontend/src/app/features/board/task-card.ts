import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { priorityLabel } from '../../shared/models/task-filters';
import { Avatar } from '../../shared/ui/avatar';
import { Chip, ChipKind } from '../../shared/ui/chip';
import { DeadlineChip } from '../../shared/ui/deadline-chip';
import { BoardTaskDto } from './models';

/**
 * A card on the board (docs/UI_UX_SPEC.md §7.4): the key and the priority, the title on at most two
 * lines, then the deadline, the counts and who it's assigned to. The column makes it draggable and
 * opens it; the card only shows it. The look is on an inner box, because the CDK moves the dragged
 * copy with a transform on the host, and the tilt of a picked-up card needs one of its own.
 */
@Component({
  selector: 'kb-task-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, Chip, DeadlineChip],
  template: `
    <div class="card">
      <div class="top">
        <span class="key">{{ task().key }}</span>
        <kb-chip [kind]="priorityKind()">{{ priority() }}</kb-chip>
      </div>
      <p class="title">{{ task().title }}</p>
      <div class="bottom">
        @if (task().dueDate; as dueDate) {
          <kb-deadline-chip [dueDate]="dueDate" [done]="done()" />
        }
        @if (task().commentCount > 0) {
          <span class="count" i18n-title title="Comentarii">
            <span class="material-symbols-outlined" aria-hidden="true">chat_bubble</span>
            {{ task().commentCount }}
          </span>
        }
        @if (task().attachmentCount > 0) {
          <span class="count" i18n-title title="Atașamente">
            <span class="material-symbols-outlined" aria-hidden="true">attach_file</span>
            {{ task().attachmentCount }}
          </span>
        }
        <span class="spacer"></span>
        @if (task().assignee; as assignee) {
          <kb-avatar [name]="assignee.displayName" [labelled]="true" />
        } @else {
          <span class="unassigned" role="img" i18n-aria-label aria-label="Neasignat"></span>
        }
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      border-radius: 8px;
      cursor: pointer;
      outline: none;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding: 10px 12px;
      border: 1px solid var(--kb-border-200);
      border-radius: 8px;
      background: var(--kb-surface-0);
      color: var(--kb-text-900);
      transition:
        box-shadow 120ms var(--kb-ease),
        border-color 120ms var(--kb-ease);
    }

    :host(:hover) .card {
      border-color: var(--kb-border-300);
      box-shadow: var(--kb-shadow-1);
    }

    :host(:focus-visible) .card {
      box-shadow: var(--kb-focus-ring);
    }

    /* Picked up: the only scale the UI spec allows (§6.0.10). */
    :host(.cdk-drag-preview) .card {
      box-shadow: var(--kb-shadow-drag);
      transform: rotate(1.5deg) scale(1.02);
    }

    /* Where it will land: an outline the size of the card. */
    :host(.cdk-drag-placeholder) .card {
      border: 1px dashed var(--kb-border-300);
      background: var(--kb-tint-100);
      box-shadow: none;
    }

    :host(.cdk-drag-placeholder) .card > * {
      visibility: hidden;
    }

    /* Picked up from the keyboard: as a card in the hand (docs/UI_UX_SPEC.md §7.4). */
    :host(.lifted) .card {
      box-shadow: var(--kb-shadow-drag), var(--kb-focus-ring);
      transform: rotate(1.5deg) scale(1.02);
    }

    :host(.cdk-drag-animating) {
      transition: transform 200ms var(--kb-ease);
    }

    .top,
    .bottom {
      display: flex;
      align-items: center;
      gap: var(--kb-space-2);
      min-height: 22px;
    }

    .top {
      justify-content: space-between;
    }

    .key {
      font-family: var(--kb-font-mono);
      font-size: 12px;
      color: var(--kb-text-500);
    }

    .title {
      display: -webkit-box;
      margin: 0;
      overflow: hidden;
      font-size: 13px;
      font-weight: 600;
      line-height: 18px;
      overflow-wrap: anywhere;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
    }

    .count {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      font-size: 12px;
      color: var(--kb-text-600);
    }

    .count .material-symbols-outlined {
      font-size: 16px;
    }

    .spacer {
      flex: 1;
    }

    .unassigned {
      width: 24px;
      height: 24px;
      flex: none;
      box-sizing: border-box;
      border: 1px dashed var(--kb-border-300);
      border-radius: var(--kb-radius-pill);
    }

    @media (prefers-reduced-motion: reduce) {
      .card,
      :host(.cdk-drag-animating) {
        transition: none;
      }
    }
  `,
})
export class TaskCard {
  readonly task = input.required<BoardTaskDto>();
  /** In a done column the deadline no longer warns. */
  readonly done = input(false);

  protected readonly priority = computed(() => priorityLabel(this.task().priority));
  protected readonly priorityKind = computed(() => this.task().priority.toLowerCase() as ChipKind);
}
