import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogClose } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { SHEET_WIDTH } from './sheet';

export interface ConfirmationRequest {
  /** What happens, as a question: "Arhivezi task-ul «Login SSO»?" */
  title: string;
  /** One sentence on what follows from it. */
  message: string;
  /** The verb, never "OK" or "Da": "Arhivează". */
  confirmLabel: string;
  icon: string;
  /** Red button and red icon: archiving, deactivating, deleting. */
  destructive?: boolean;
}

/**
 * A confirmation sheet (docs/UI_UX_SPEC.md §6.0.5, §6.6): the icon left of the title, then
 * [Anulează] [verb] at the bottom right. Esc and the backdrop cancel. A destructive one starts with
 * the focus on Anulează, so Enter alone can't do the damage.
 */
@Component({
  selector: 'kb-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatDialogClose],
  template: `
    <div class="head">
      <span class="badge" [class.danger]="request.destructive" aria-hidden="true"
        ><span class="material-symbols-outlined">{{ request.icon }}</span></span
      >
      <div>
        <h2 id="kb-confirm-title" class="kb-title">{{ request.title }}</h2>
        <p id="kb-confirm-message" class="message">{{ request.message }}</p>
      </div>
    </div>
    <div class="actions">
      <button
        matButton="outlined"
        class="kb-bezel"
        type="button"
        [mat-dialog-close]="false"
        [attr.cdkFocusInitial]="request.destructive ? '' : null"
        i18n
      >
        Anulează
      </button>
      <button
        matButton="filled"
        type="button"
        [class.kb-default]="!request.destructive"
        [class.kb-danger]="request.destructive"
        [mat-dialog-close]="true"
        [attr.cdkFocusInitial]="request.destructive ? null : ''"
      >
        {{ request.confirmLabel }}
      </button>
    </div>
  `,
  styles: `
    :host {
      display: block;
      padding: var(--kb-space-6);
    }

    .head {
      display: flex;
      gap: var(--kb-space-4);
    }

    .badge {
      display: grid;
      place-items: center;
      flex: none;
      width: 40px;
      height: 40px;
      border-radius: var(--kb-radius-pill);
      background: var(--kb-tint-100);
      color: var(--kb-accent-600);
    }

    .badge.danger {
      background: var(--kb-danger-bg);
      color: var(--kb-danger-600);
    }

    .badge .material-symbols-outlined {
      font-size: 20px;
    }

    h2 {
      margin: 0;
      padding: 0;
    }

    .message {
      margin: var(--kb-space-1) 0 0;
      color: var(--kb-text-600);
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--kb-space-2);
      margin-top: var(--kb-space-6);
    }
  `,
})
export class ConfirmDialog {
  protected readonly request = inject<ConfirmationRequest>(MAT_DIALOG_DATA);
}

/** Asks before something that can't be taken back. Resolves true only if the user said yes. */
@Injectable({ providedIn: 'root' })
export class Confirmation {
  private readonly dialog = inject(MatDialog);

  async ask(request: ConfirmationRequest): Promise<boolean> {
    const opened = this.dialog.open<ConfirmDialog, ConfirmationRequest, boolean>(ConfirmDialog, {
      data: request,
      width: SHEET_WIDTH.confirm,
      role: 'alertdialog',
      ariaLabelledBy: 'kb-confirm-title',
      ariaDescribedBy: 'kb-confirm-message',
    });
    return (await firstValueFrom(opened.afterClosed())) === true;
  }
}
