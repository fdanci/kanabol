import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A placeholder block in the shape of what's loading (docs/UI_UX_SPEC.md §6.8): never a spinner over
 * the page. It shimmers for 1.2s, and stays still for those who ask for less motion.
 */
@Component({
  selector: 'kb-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
  host: {
    'aria-hidden': 'true',
    '[style.width]': 'width()',
    '[style.height]': 'height()',
    '[class.circle]': 'circle()',
  },
  styles: `
    :host {
      --_block: color-mix(in srgb, var(--kb-navy-900) 8%, var(--kb-surface-0));
      --_light: color-mix(in srgb, var(--kb-navy-900) 3%, var(--kb-surface-0));
      display: block;
      border-radius: var(--kb-radius-sm);
      background: linear-gradient(90deg, var(--_block) 0%, var(--_light) 50%, var(--_block) 100%);
      background-size: 200% 100%;
      animation: shimmer 1.2s linear infinite;
    }

    :host(.circle) {
      border-radius: var(--kb-radius-pill);
    }

    @keyframes shimmer {
      from {
        background-position: 100% 0;
      }
      to {
        background-position: -100% 0;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      :host {
        animation: none;
        background: var(--_block);
      }
    }
  `,
})
export class Skeleton {
  readonly width = input('100%');
  readonly height = input('14px');
  readonly circle = input(false);
}
