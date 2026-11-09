import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { Skeleton } from './skeleton';

const NUMBER = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 0 });

/**
 * A stat tile (docs/UI_UX_SPEC.md §7.2): a flat card with a caption, a 28px/600 navy number and a caption
 * under it. With a link it's a clickable card (§6.0.10); while the number is on its way, null, a skeleton
 * keeps its place. The reports give it a text instead of a number, a duration or a rate (§7.8).
 */
@Component({
  selector: 'kb-stat-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, Skeleton],
  template: `
    <ng-template #content>
      <span class="label">{{ label() }}</span>
      <span class="value">
        @if (value() === null) {
          <kb-skeleton width="48px" height="28px" />
        } @else {
          {{ shown() }}
        }
      </span>
      <span class="caption">{{ caption() }}</span>
    </ng-template>
    @if (link(); as link) {
      <a class="tile clickable" [routerLink]="link" [queryParams]="queryParams()">
        <ng-container [ngTemplateOutlet]="content" />
      </a>
    } @else {
      <div class="tile"><ng-container [ngTemplateOutlet]="content" /></div>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .tile {
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-1);
      height: 100%;
      padding: var(--kb-space-4) 20px;
      background: var(--kb-surface-0);
      border: 1px solid var(--kb-border-200);
      border-radius: var(--kb-radius-md);
      color: inherit;
      text-decoration: none;
      transition:
        box-shadow var(--kb-dur-fast) var(--kb-ease),
        border-color var(--kb-dur-fast) var(--kb-ease);
    }

    .clickable:hover {
      text-decoration: none;
      box-shadow: var(--kb-shadow-1);
      border-color: var(--kb-border-300);
    }

    .label,
    .caption {
      font-size: 12px;
      line-height: 16px;
      color: var(--kb-text-600);
    }

    .label {
      font-weight: 600;
    }

    .value {
      display: flex;
      align-items: center;
      min-height: 36px;
      font-size: 28px;
      line-height: 36px;
      font-weight: 600;
      color: var(--kb-navy-900);
      font-variant-numeric: tabular-nums;
    }

    @media (prefers-reduced-motion: reduce) {
      .tile {
        transition: none;
      }
    }
  `,
})
export class StatTile {
  readonly label = input.required<string>();
  /** A number, formatted in ro-RO, or a text shown as it is, such as a duration; null until it comes. */
  readonly value = input<number | string | null>(null);
  readonly caption = input.required<string>();
  readonly link = input<string | null>(null);
  readonly queryParams = input<Params | null>(null);

  protected readonly shown = computed(() => {
    const value = this.value();
    return typeof value === 'string' ? value : NUMBER.format(value ?? 0);
  });
}
