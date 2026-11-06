import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

export interface Segment {
  label: string;
  /** Relative to the route the control sits in, as in routerLink. */
  link: string;
}

/**
 * The views of one thing, side by side (docs/UI_UX_SPEC.md §6.0.3, §6.7): a 28px track with the
 * active one raised in white. Each segment is a link, so the view is in the address.
 */
@Component({
  selector: 'kb-segmented-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav [attr.aria-label]="label()">
      @for (segment of segments(); track segment.link) {
        <a [routerLink]="segment.link" routerLinkActive="active" ariaCurrentWhenActive="page">{{
          segment.label
        }}</a>
      }
    </nav>
  `,
  styles: `
    :host {
      display: inline-block;
    }

    nav {
      display: flex;
      gap: 2px;
      height: 28px;
      padding: 2px;
      border-radius: 7px;
      background: color-mix(in srgb, var(--kb-navy-900) 6%, var(--kb-surface-0));
    }

    a {
      display: inline-flex;
      align-items: center;
      padding: 0 var(--kb-space-3);
      border-radius: 5px;
      color: var(--kb-text-700);
      font-size: 13px;
      font-weight: 500;
      text-decoration: none;
      white-space: nowrap;
      transition:
        background-color 200ms var(--kb-ease),
        box-shadow 200ms var(--kb-ease);
    }

    a:hover {
      color: var(--kb-text-900);
      text-decoration: none;
    }

    a.active {
      background: var(--kb-surface-0);
      box-shadow: var(--kb-shadow-bezel);
      color: var(--kb-text-900);
    }

    /* The global focus ring, kept on the raised segment too. */
    a:focus-visible {
      border-radius: 5px;
      box-shadow: var(--kb-focus-ring);
    }

    a.active:focus-visible {
      box-shadow: var(--kb-shadow-bezel), var(--kb-focus-ring);
    }

    @media (prefers-reduced-motion: reduce) {
      a {
        transition: none;
      }
    }
  `,
})
export class SegmentedNav {
  readonly segments = input.required<Segment[]>();
  readonly label = input.required<string>();
}
