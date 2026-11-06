import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A list with nothing in it yet (docs/UI_UX_SPEC.md §6.8): a 48px icon in a tint-100 circle, a title,
 * a sentence, and the action that fills it, for those who may take it.
 *
 * <kb-empty-state icon="task_alt" title="Niciun task încă">
 *   <p>…</p>
 *   <button kbEmptyAction …>Creează primul task</button>
 * </kb-empty-state>
 */
@Component({
  selector: 'kb-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="circle" aria-hidden="true"
      ><span class="material-symbols-outlined">{{ icon() }}</span></span
    >
    <h3 class="kb-subtitle">{{ title() }}</h3>
    <div class="text"><ng-content /></div>
    <div class="action"><ng-content select="[kbEmptyAction]" /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--kb-space-2);
      padding: var(--kb-space-12) var(--kb-space-6);
      text-align: center;
    }

    .circle {
      display: grid;
      place-items: center;
      width: 80px;
      height: 80px;
      margin-bottom: var(--kb-space-2);
      border-radius: var(--kb-radius-pill);
      background: var(--kb-tint-100);
    }

    .material-symbols-outlined {
      font-size: 48px;
      color: var(--kb-archived-400);
    }

    h3 {
      margin: 0;
    }

    .text {
      max-width: 420px;
      color: var(--kb-text-600);
    }

    .action:empty {
      display: none;
    }

    .action {
      margin-top: var(--kb-space-2);
    }
  `,
})
export class EmptyState {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
}
