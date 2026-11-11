import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MoveQuestion } from './estimate-sheet';
import { Field } from './field';

/** A pause's reason, as long as the API takes (docs/SPEC.md §5.1). */
export const PAUSE_REASON_MAX = 500;

function reasonProblem(value: string): string | null {
  return value.trim() === '' ? $localize`Scrie motivul pauzei.` : null;
}

/**
 * Every task put in În pauză (docs/UI_UX_SPEC.md §7.4): a 440px sheet asks why, one line of at most 500
 * characters, counted under the field. It closes with the reason, trimmed, or with nothing on "Renunță",
 * Esc or a click beside it, which put the card back.
 */
@Component({
  selector: 'kb-pause-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton],
  template: `
    <form (submit)="submit($event)" novalidate>
      <h2 id="kb-sheet-title" class="kb-title" i18n>De ce pui pe pauză «{{ data.key }}»?</h2>
      <div class="kb-form-group">
        <div class="kb-form-row">
          <label class="kb-form-label kb-required" for="kb-pause-reason" i18n>Motiv</label>
          <div>
            <input
              #reasonInput
              id="kb-pause-reason"
              class="kb-field"
              type="text"
              autocomplete="off"
              [attr.maxlength]="max"
              [value]="reason.value()"
              [attr.aria-invalid]="reason.error() !== null"
              [attr.aria-describedby]="
                reason.error() !== null ? 'kb-pause-error kb-pause-count' : 'kb-pause-count'
              "
              (input)="reason.value.set(reasonInput.value)"
              (blur)="reason.touched.set(true)"
            />
            @if (reason.error(); as message) {
              <p id="kb-pause-error" class="kb-field-error">{{ message }}</p>
            }
            <p id="kb-pause-count" class="kb-form-help count">
              {{ reason.value().length }}/{{ max }}
            </p>
          </div>
        </div>
      </div>
      <div class="actions">
        <button matButton="outlined" class="kb-bezel" type="button" (click)="ref.close()" i18n>
          Renunță
        </button>
        <button matButton="filled" class="kb-default" type="submit" i18n>Pune pe pauză</button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
      padding: var(--kb-space-6);
    }

    h2 {
      margin: 0;
      overflow-wrap: anywhere;
    }

    .kb-form-group {
      margin-top: var(--kb-space-4);
    }

    .count {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--kb-space-2);
      margin-top: var(--kb-space-6);
    }
  `,
})
export class PauseSheet {
  protected readonly data = inject<MoveQuestion>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<PauseSheet, string>>(MatDialogRef);
  protected readonly max = PAUSE_REASON_MAX;
  private readonly submitted = signal(false);
  protected readonly reason = new Field('', reasonProblem, this.submitted);

  protected submit(event: Event): void {
    event.preventDefault();
    this.submitted.set(true);
    if (this.reason.error() === null) {
      this.ref.close(this.reason.value().trim());
    }
  }
}
