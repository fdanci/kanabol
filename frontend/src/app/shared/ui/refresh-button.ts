import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';

/**
 * "Reîncarcă" for a tab of the task's panel (docs/UI_UX_SPEC.md §7.5): 28px with a 16px refresh icon, its
 * tooltip and name the same. While the tab reads, the icon turns and the button is off; under
 * prefers-reduced-motion it doesn't turn.
 */
@Component({
  selector: 'kb-refresh-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, MatIcon, MatTooltip],
  template: `
    <button
      matIconButton
      type="button"
      i18n-matTooltip
      matTooltip="Reîncarcă"
      i18n-aria-label
      aria-label="Reîncarcă"
      [class.turning]="busy()"
      [disabled]="busy()"
      (click)="refresh.emit()"
    >
      <mat-icon>refresh</mat-icon>
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .turning mat-icon {
      animation: kb-turn 900ms linear infinite;
    }

    @keyframes kb-turn {
      to {
        transform: rotate(360deg);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .turning mat-icon {
        animation: none;
      }
    }
  `,
})
export class RefreshButton {
  /** The tab is reading: the icon turns, the button waits. */
  readonly busy = input(false);
  readonly refresh = output();
}
