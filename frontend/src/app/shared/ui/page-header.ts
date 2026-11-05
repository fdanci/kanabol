import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface Crumb {
  label: string;
  /** Absent on the last one, which is the page itself. */
  link?: string;
}

/**
 * The top of a page (docs/UI_UX_SPEC.md §5): the h1, the breadcrumb under it, and on the right the
 * page's one default button, which comes from inside: <kb-page-header …><button …></kb-page-header>.
 */
@Component({
  selector: 'kb-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="titles">
      <h1 class="kb-display">{{ title() }}</h1>
      @if (crumbs().length > 0) {
        <nav class="kb-caption" i18n-aria-label aria-label="Unde ești">
          <ol>
            @for (crumb of crumbs(); track $index) {
              <li>
                @if (crumb.link && !$last) {
                  <a [routerLink]="crumb.link">{{ crumb.label }}</a>
                } @else {
                  <span [attr.aria-current]="$last ? 'page' : null">{{ crumb.label }}</span>
                }
              </li>
            }
          </ol>
        </nav>
      }
    </div>
    <div class="actions"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--kb-space-4);
      padding: var(--kb-space-6) var(--kb-space-6) var(--kb-space-4);
    }

    h1 {
      margin: 0;
    }

    ol {
      display: flex;
      flex-wrap: wrap;
      margin: 2px 0 0;
      padding: 0;
      list-style: none;
    }

    li + li::before {
      content: '›';
      margin: 0 6px;
      color: var(--kb-text-500);
    }

    a {
      color: var(--kb-text-600);
    }

    [aria-current='page'] {
      color: var(--kb-text-700);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: var(--kb-space-2);
    }
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly crumbs = input<Crumb[]>([]);
}
