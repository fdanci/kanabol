import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** The first letters of the first two words: "Ana Maria Pop" is "AM", "ana@example.com" is "A". */
export function initialsOf(name: string): string {
  return name
    .trim()
    .split(/[\s@._-]+/)
    .filter((word) => word.length > 0)
    .slice(0, 2)
    .map((word) => word[0].toLocaleUpperCase('ro-RO'))
    .join('');
}

/**
 * Initials on tint-100, 24px in the toolbar and 32px in lists (docs/UI_UX_SPEC.md §6.0.8). Next to
 * the name it repeats nothing for a screen reader; alone, it says whose it is.
 */
@Component({
  selector: 'kb-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ initials() }}`,
  host: {
    '[class.large]': 'size() === 32',
    '[attr.role]': "labelled() ? 'img' : null",
    '[attr.aria-label]': 'labelled() ? name() : null',
    '[attr.aria-hidden]': "labelled() ? null : 'true'",
  },
  styles: `
    :host {
      display: inline-grid;
      place-items: center;
      flex: none;
      width: 24px;
      height: 24px;
      border-radius: var(--kb-radius-pill);
      background: var(--kb-tint-100);
      color: var(--kb-text-900);
      font-size: 10px;
      font-weight: 600;
      line-height: 1;
      user-select: none;
    }

    :host(.large) {
      width: 32px;
      height: 32px;
      font-size: 12px;
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();
  readonly size = input<24 | 32>(24);
  /** Whether the name isn't written next to it, so the avatar has to say it. */
  readonly labelled = input(false);

  protected readonly initials = computed(() => initialsOf(this.name()));
}
