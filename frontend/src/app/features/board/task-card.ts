import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { priorityLabel } from '../../shared/models/task-filters';
import { AvatarStack } from '../../shared/ui/avatar-stack';
import { Chip, ChipKind } from '../../shared/ui/chip';
import { DeadlineChip } from '../../shared/ui/deadline-chip';
import { BoardTaskDto } from './models';

/**
 * A card on the board (docs/UI_UX_SPEC.md §7.4): the key and the priority, the title on at most two
 * lines, then the deadline, the counts and everyone it's assigned to. The column makes it draggable and
 * opens it; the card only shows it. The look is on an inner box, because the CDK moves the dragged
 * copy with a transform on the host, and the tilt of a picked-up card needs one of its own.
 */
@Component({
  selector: 'kb-task-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarStack, Chip, DeadlineChip],
  // Two names for one pulse, so the next change starts it again (docs/UI_UX_SPEC.md §7.4).
  host: {
    '[class.pulse-a]': 'pulse() % 2 === 1',
    '[class.pulse-b]': 'pulse() > 0 && pulse() % 2 === 0',
  },
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
        @if (task().assignees.length > 0) {
          <kb-avatar-stack [people]="task().assignees" />
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

    /* Changed by someone else: the card pulses once with tint-100, in 600 ms (UI spec §7.4, §8). */
    :host(.pulse-a) .card {
      animation: kb-pulse-a 600ms var(--kb-ease);
    }

    :host(.pulse-b) .card {
      animation: kb-pulse-b 600ms var(--kb-ease);
    }

    @keyframes kb-pulse-a {
      35% {
        background: var(--kb-tint-100);
      }
    }

    @keyframes kb-pulse-b {
      35% {
        background: var(--kb-tint-100);
      }
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

      :host(.pulse-a) .card,
      :host(.pulse-b) .card {
        animation: none;
      }
    }
  `,
})
export class TaskCard {
  readonly task = input.required<BoardTaskDto>();
  /** In a done column the deadline no longer warns. */
  readonly done = input(false);
  /** How many times someone else changed it: each change pulses it once. */
  readonly pulse = input(0);

  protected readonly priority = computed(() => priorityLabel(this.task().priority));
  protected readonly priorityKind = computed(() => this.task().priority.toLowerCase() as ChipKind);
}
