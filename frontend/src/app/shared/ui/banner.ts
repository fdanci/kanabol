import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatIconButton } from '@angular/material/button';

export type BannerVariant = 'info' | 'success' | 'warning' | 'danger';

const ICONS: Record<BannerVariant, string> = {
  info: 'info',
  success: 'check_circle',
  warning: 'warning',
  danger: 'error',
};

/**
 * Lasting context on a page, such as "Acest proiect e arhivat" (docs/UI_UX_SPEC.md §6.1). Feedback on
 * an action goes in the snackbar instead.
 */
@Component({
  selector: 'kb-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton],
  host: {
    '[class]': 'variant()',
    '[attr.role]': "variant() === 'danger' || variant() === 'warning' ? 'alert' : 'status'",
  },
  template: `
    <span class="icon material-symbols-outlined" aria-hidden="true">{{ icon() }}</span>
    <div class="text"><ng-content /></div>
    @if (dismissible()) {
      <button
        matIconButton
        type="button"
        class="close"
        i18n-aria-label
        aria-label="Închide"
        (click)="dismissed.emit()"
      >
        <span class="material-symbols-outlined" aria-hidden="true">close</span>
      </button>
    }
  `,
  styles: `
    :host {
      --_bg: var(--kb-info-bg);
      --_fg: var(--kb-info-text);
      display: flex;
      align-items: flex-start;
      gap: var(--kb-space-3);
      padding: var(--kb-space-3) var(--kb-space-4);
      border: 1px solid color-mix(in srgb, var(--_fg) 30%, transparent);
      border-radius: var(--kb-radius-md);
      background: var(--_bg);
      color: var(--_fg);
      font-size: 14px;
      line-height: 20px;
    }

    :host(.success) {
      --_bg: var(--kb-success-bg);
      --_fg: var(--kb-success-600);
    }

    :host(.warning) {
      --_bg: var(--kb-warning-bg);
      --_fg: var(--kb-warning-600);
    }

    :host(.danger) {
      --_bg: var(--kb-danger-bg);
      --_fg: var(--kb-danger-600);
    }

    .icon {
      font-size: 20px;
    }

    .text {
      flex: 1;
      min-width: 0;
    }

    .close {
      margin: -6px -8px -6px 0;
      color: inherit;
    }

    .close .material-symbols-outlined {
      font-size: 16px;
    }
  `,
})
export class Banner {
  readonly variant = input<BannerVariant>('info');
  readonly dismissible = input(false);
  readonly dismissed = output();

  protected readonly icon = computed(() => ICONS[this.variant()]);
}
