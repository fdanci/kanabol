import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatOption, MatSelect } from '@angular/material/select';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { AssignableRoleDto, UserDto } from './models';
import { Field } from '../../shared/ui/field';
import { roleProblem } from './user-fields';
import { UsersStore } from './users-store';

/**
 * Another role for a user, among those the caller may give (docs/SPEC.md §4.4). Changing it ends the
 * user's sessions, so the new permissions come with the next sign-in.
 */
@Component({
  selector: 'kb-role-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatSelect, MatOption],
  template: `
    <form (submit)="submit($event)" novalidate>
      <h2 id="kb-sheet-title" class="kb-title" i18n>Schimbi rolul lui «{{ user.displayName }}»</h2>
      <p class="lead" i18n>
        Sesiunile lui se închid, iar noile permisiuni se aplică de la următoarea intrare în cont.
      </p>

      <div class="kb-form-group">
        <div class="kb-form-row">
          <span id="user-role-label" class="kb-form-label kb-required" i18n>Rol</span>
          <div>
            <mat-select
              id="user-role"
              class="kb-field kb-select"
              panelClass="kb-menu"
              aria-labelledby="user-role-label"
              [required]="true"
              [value]="role.value()"
              (valueChange)="role.value.set($event)"
            >
              @for (option of roles(); track option.id) {
                <mat-option [value]="option.id">{{ option.name }}</mat-option>
              }
            </mat-select>
            @if (role.error(); as message) {
              <p id="user-role-error" class="kb-field-error">{{ message }}</p>
            }
          </div>
        </div>
      </div>

      <div class="actions">
        <button matButton="outlined" class="kb-bezel" type="button" (click)="cancel()" i18n>
          Anulează
        </button>
        <button matButton="filled" class="kb-default" type="submit" [disabled]="busy()" i18n>
          Schimbă rolul
        </button>
      </div>
    </form>
  `,
  styleUrl: './sheet.scss',
})
export class RoleSheet {
  private readonly store = inject(UsersStore);
  private readonly ref = inject<MatDialogRef<RoleSheet, UserDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  protected readonly user = inject<{ user: UserDto }>(MAT_DIALOG_DATA).user;

  protected readonly roles = signal<AssignableRoleDto[]>([]);
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);
  protected readonly role = new Field(this.user.roleId, roleProblem, this.submitted);

  constructor() {
    askBeforeClosing(this.ref, () => this.role.changed(), this.confirmation);
    void this.loadRoles();
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.role.error() !== null) {
      return;
    }
    if (!this.role.changed()) {
      this.ref.close();
      return;
    }

    this.busy.set(true);
    try {
      this.ref.close(await this.store.changeRole(this.user.id, this.role.value()));
    } catch {
      // errorInterceptor has said why, such as the last Admin or a role beyond the caller's.
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.role.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }

  private async loadRoles(): Promise<void> {
    try {
      const roles = await this.store.assignableRoles();
      this.roles.set(roles);
      // A current role beyond the caller's isn't offered, so nothing shows as chosen.
      if (!roles.some((role) => role.id === this.user.roleId)) {
        this.role.value.set('');
      }
    } catch {
      // errorInterceptor has said why.
    }
  }
}
