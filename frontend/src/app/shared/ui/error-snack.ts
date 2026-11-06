import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import {
  MAT_SNACK_BAR_DATA,
  MatSnackBarAction,
  MatSnackBarActions,
  MatSnackBarLabel,
  MatSnackBarRef,
} from '@angular/material/snack-bar';

export interface ErrorSnackData {
  message: string;
  correlationId: string | null;
}

/**
 * The error variant of the snackbar (docs/UI_UX_SPEC.md §6.2): an error icon, the message, and the
 * last 6 characters of the correlation id, which Copiază copies whole for whoever reads the logs.
 */
@Component({
  selector: 'kb-error-snack',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatSnackBarLabel, MatSnackBarActions, MatSnackBarAction],
  template: `
    <div matSnackBarLabel class="label">
      <span class="material-symbols-outlined icon" aria-hidden="true">error</span>
      <span
        >{{ data.message }}
        @if (code) {
          <span class="code" i18n
            >Cod: <span class="kb-mono">{{ code }}</span></span
          >
        }
      </span>
    </div>
    @if (data.correlationId) {
      <div matSnackBarActions>
        <button matButton matSnackBarAction type="button" (click)="copy()" i18n>Copiază</button>
      </div>
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      width: 100%;
    }

    .label {
      display: flex;
      align-items: flex-start;
      gap: var(--kb-space-2);
    }

    .icon {
      font-size: 20px;
      color: color-mix(in srgb, var(--kb-fill-danger) 45%, white);
    }

    .code {
      white-space: nowrap;
    }

    .kb-mono {
      font-size: 12px;
    }
  `,
})
export class ErrorSnack {
  protected readonly data = inject<ErrorSnackData>(MAT_SNACK_BAR_DATA);
  private readonly ref = inject(MatSnackBarRef);
  protected readonly code = this.data.correlationId?.slice(-6) ?? null;

  protected copy(): void {
    if (this.data.correlationId) {
      void navigator.clipboard.writeText(this.data.correlationId);
    }
    this.ref.dismissWithAction();
  }
}
