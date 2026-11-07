import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';
import { Avatar } from './avatar';

/** Someone in a stack: the people a task is assigned to, as the API lists them. */
export interface StackedPerson {
  userId: string;
  displayName: string;
}

/** How many avatars a stack shows before "+N" (docs/UI_UX_SPEC.md §7.3, §7.4). */
export const STACK_MAX = 3;

/**
 * Overlapping avatars of 24px, the next one over the last, at most three and then "+N"
 * (docs/UI_UX_SPEC.md §7.3, §7.4). Every name is in the tooltip and, once, in the aria-label, so
 * the avatars and the count stay quiet. For nobody it shows and says nothing: the card and the list
 * each say "Neasignat" their own way.
 */
@Component({
  selector: 'kb-avatar-stack',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, MatTooltip],
  template: `
    @if (people().length > 0) {
      <span class="stack" [matTooltip]="names()">
        @for (person of shown(); track person.userId) {
          <kb-avatar [name]="person.displayName" />
        }
        @if (more() > 0) {
          <span class="more" aria-hidden="true">+{{ more() }}</span>
        }
      </span>
    }
  `,
  host: {
    '[attr.role]': "people().length > 0 ? 'img' : null",
    '[attr.aria-label]': 'people().length > 0 ? names() : null',
  },
  styles: `
    :host {
      display: inline-flex;
      flex: none;
    }

    .stack {
      display: inline-flex;
      align-items: center;
    }

    kb-avatar {
      box-shadow: 0 0 0 2px var(--kb-surface-0);
    }

    kb-avatar + kb-avatar {
      margin-left: -6px;
    }

    .more {
      margin-left: var(--kb-space-1);
      font-size: 12px;
      color: var(--kb-text-600);
      white-space: nowrap;
    }
  `,
})
export class AvatarStack {
  readonly people = input.required<readonly StackedPerson[]>();

  protected readonly shown = computed(() => this.people().slice(0, STACK_MAX));
  protected readonly more = computed(() => this.people().length - this.shown().length);
  protected readonly names = computed(() =>
    this.people()
      .map((person) => person.displayName)
      .join(', '),
  );
}
