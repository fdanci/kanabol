import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The mark and the wordmark (docs/UI_UX_SPEC.md §14): inline, so it costs no request. The geometry
 * is §14.1's, on the mark's own viewBox. Mark 24px high, text 17px, gap 8px. The mark takes its
 * own colors, not the theme's (§14.4): logo-bar for the bar and the inner discs, navy in light and
 * white in dark, and logo-disc for the outer discs in both, as kanabol-mark-on-dark.svg has them.
 */
@Component({
  selector: 'kb-lockup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="mark" viewBox="8 28 84 44" aria-hidden="true" focusable="false">
      <rect class="disc" x="12" y="32" width="11" height="36" rx="3" />
      <rect class="disc" x="77" y="32" width="11" height="36" rx="3" />
      <rect class="body" x="30" y="47" width="40" height="6" rx="3" />
      <rect class="body" x="24" y="38" width="9" height="24" rx="3" />
      <rect class="body" x="67" y="38" width="9" height="24" rx="3" />
      <rect class="line" x="15" y="38" width="5" height="3" rx="1" />
      <rect class="line" x="15" y="44" width="5" height="3" rx="1" />
      <rect class="line" x="80" y="38" width="5" height="3" rx="1" />
      <rect class="line" x="80" y="44" width="5" height="3" rx="1" />
    </svg>
    <span class="word">Kanabol</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      line-height: 1;
    }

    .mark {
      display: block;
      width: auto;
      height: 24px;
    }

    .disc {
      fill: var(--kb-logo-disc);
    }

    .body {
      fill: var(--kb-logo-bar);
    }

    .line {
      fill: var(--kb-fill-shade);
    }

    .word {
      font-size: 17px;
      font-weight: 700;
      letter-spacing: -0.015em;
      color: var(--kb-brand-800);
    }

    :host-context(html[data-theme='dark']) .word {
      color: var(--kb-text-900);
    }

    @media (prefers-color-scheme: dark) {
      :host-context(html:not([data-theme='light'])) .word {
        color: var(--kb-text-900);
      }
    }
  `,
})
export class Lockup {}
