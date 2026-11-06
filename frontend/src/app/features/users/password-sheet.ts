import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PASSWORD_MAX } from '../../shared/models/password-policy';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { UserDto } from './models';
import { Field, passwordProblem, refuseFields } from './user-fields';
import { UsersStore } from './users-store';

/**
 * An initial password for someone else (docs/SPEC.md §4.4): they change it at their next sign-in,
 * their sessions end and the lockout is lifted. Nobody resets their own password this way.
 */
@Component({
  selector: 'kb-password-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton],
  template: `
    <form (submit)="submit($event)" novalidate>
      <h2 id="kb-sheet-title" class="kb-title" i18n>
        Resetezi parola lui «{{ user.displayName }}»?
      </h2>
      <p class="lead" i18n>
        Primește o parolă inițială, pe care o schimbă la prima intrare. Sesiunile lui se închid, iar
        contul se deblochează.
      </p>

      <div class="kb-form-group">
        <div class="kb-form-row">
          <label class="kb-form-label kb-required" for="user-password" i18n>Parolă inițială</label>
          <div>
            <span class="password">
              <input
                #passwordInput
                id="user-password"
                class="kb-field"
                autocomplete="new-password"
                [type]="shown() ? 'text' : 'password'"
                [attr.maxlength]="passwordMax"
                [value]="password.value()"
                [attr.aria-invalid]="password.error() !== null"
                [attr.aria-describedby]="password.error() !== null ? 'user-password-error' : null"
                (input)="password.value.set(passwordInput.value)"
                (blur)="password.touched.set(true)"
              />
              <button
                matButton
                class="kb-sm reveal"
                type="button"
                [attr.aria-pressed]="shown()"
                (click)="shown.set(!shown())"
                i18n
              >
                arată
              </button>
            </span>
            @if (password.error(); as message) {
              <p id="user-password-error" class="kb-field-error">{{ message }}</p>
            }
          </div>
        </div>
      </div>

      <div class="actions">
        <button matButton="outlined" class="kb-bezel" type="button" (click)="cancel()" i18n>
          Anulează
        </button>
        <button matButton="filled" class="kb-default" type="submit" [disabled]="busy()" i18n>
          Resetează parola
        </button>
      </div>
    </form>
  `,
  styleUrl: './sheet.scss',
})
export class PasswordSheet {
  private readonly store = inject(UsersStore);
  private readonly ref = inject<MatDialogRef<PasswordSheet, UserDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);
  protected readonly user = inject<{ user: UserDto }>(MAT_DIALOG_DATA).user;

  protected readonly passwordMax = PASSWORD_MAX;
  protected readonly busy = signal(false);
  protected readonly shown = signal(false);
  private readonly submitted = signal(false);
  protected readonly password = new Field('', passwordProblem, this.submitted);

  constructor() {
    askBeforeClosing(this.ref, () => this.password.changed(), this.confirmation);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.password.error() !== null) {
      return;
    }

    this.busy.set(true);
    try {
      this.ref.close(await this.store.resetPassword(this.user.id, this.password.value()));
    } catch (error) {
      if (!refuseFields(error, { initialPassword: this.password })) {
        this.snackbar.error($localize`Parola nu a fost resetată. Reîncearcă.`, null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.password.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }
}
