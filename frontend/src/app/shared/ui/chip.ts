import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type ChipKind =
  | 'neutral'
  | 'low'
  | 'medium'
  | 'high'
  | 'critical'
  | 'owner'
  | 'member'
  | 'archived'
  | 'deadline-ok'
  | 'deadline-soon'
  | 'deadline-overdue'
  | 'inactive'
  | 'locked'
  | 'password-change';

/** Priority and deadline carry an icon, so color isn't their only sign (docs/UI_UX_SPEC.md §9). */
const ICONS: Partial<Record<ChipKind, string>> = {
  low: 'arrow_downward',
  medium: 'remove',
  high: 'arrow_upward',
  critical: 'priority_high',
  'deadline-ok': 'event',
  'deadline-soon': 'schedule',
  'deadline-overdue': 'event_busy',
  locked: 'lock_clock',
  'password-change': 'key',
};

/** A state label: 22px, pill, no border (docs/UI_UX_SPEC.md §6.0.11, §6.5). The text comes from inside. */
@Component({
  selector: 'kb-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'kind()' },
  template: `
    @if (icon(); as icon) {
      <span class="material-symbols-outlined" aria-hidden="true">{{ icon }}</span>
    }
    <ng-content />
  `,
  styles: `
    :host {
      --_bg: color-mix(in srgb, var(--kb-navy-900) 6%, var(--kb-surface-0));
      --_fg: var(--kb-text-700);
      display: inline-flex;
      align-items: center;
      gap: 4px;
      height: 22px;
      padding: 0 var(--kb-space-2);
      border-radius: var(--kb-radius-pill);
      background: var(--_bg);
      color: var(--_fg);
      font-size: 12px;
      font-weight: 500;
      line-height: 1;
      white-space: nowrap;
      vertical-align: middle;
    }

    .material-symbols-outlined {
      font-size: 14px;
    }

    /* A deactivated account: text-600, which passes AA on the neutral background in both themes,
       where archived-400 has about 2:1. */
    :host(.low),
    :host(.inactive) {
      --_fg: var(--kb-text-600);
    }

    :host(.medium),
    :host(.password-change) {
      --_bg: var(--kb-tint-100);
      --_fg: var(--kb-accent-600);
    }

    :host(.high),
    :host(.deadline-soon),
    :host(.locked) {
      --_bg: var(--kb-warning-bg);
      --_fg: var(--kb-warning-600);
    }

    :host(.critical),
    :host(.deadline-overdue) {
      --_bg: var(--kb-danger-bg);
      --_fg: var(--kb-danger-600);
    }

    :host(.deadline-ok) {
      --_bg: var(--kb-success-bg);
      --_fg: var(--kb-success-600);
    }

    :host(.owner) {
      --_bg: var(--kb-sky-300);
      --_fg: var(--kb-navy-900);
    }

    :host(.archived) {
      --_fg: var(--kb-archived-400);
    }
  `,
})
export class Chip {
  readonly kind = input<ChipKind>('neutral');

  protected readonly icon = computed(() => ICONS[this.kind()] ?? null);
}
