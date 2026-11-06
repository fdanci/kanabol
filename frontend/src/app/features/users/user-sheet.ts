import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatOption, MatSelect } from '@angular/material/select';
import { PASSWORD_MAX } from '../../shared/models/password-policy';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { AssignableRoleDto, UserDto } from './models';
import {
  EMAIL_MAX,
  Field,
  NAME_MAX,
  emailProblem,
  nameProblem,
  passwordProblem,
  refuseFields,
  roleProblem,
} from './user-fields';
import { UsersStore } from './users-store';

export interface UserSheetData {
  /** Null to create one. */
  user: UserDto | null;
}

/**
 * A user's email and name, in a 600px sheet with the labels on the left (docs/UI_UX_SPEC.md §7.7).
 * A new user also gets a role, among those the caller may give, and an initial password. It closes
 * with the user as the API saved it.
 */
@Component({
  selector: 'kb-user-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatSelect, MatOption],
  templateUrl: './user-sheet.html',
  styleUrl: './sheet.scss',
})
export class UserSheet {
  private readonly store = inject(UsersStore);
  private readonly ref = inject<MatDialogRef<UserSheet, UserDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);
  private readonly user = inject<UserSheetData>(MAT_DIALOG_DATA).user;

  protected readonly creating = this.user === null;
  protected readonly emailMax = EMAIL_MAX;
  protected readonly nameMax = NAME_MAX;
  protected readonly passwordMax = PASSWORD_MAX;
  protected readonly busy = signal(false);
  protected readonly shown = signal(false);
  protected readonly roles = signal<AssignableRoleDto[]>([]);
  private readonly submitted = signal(false);

  protected readonly email = new Field(this.user?.email ?? '', emailProblem, this.submitted);
  protected readonly name = new Field(this.user?.displayName ?? '', nameProblem, this.submitted);
  protected readonly role = new Field('', roleProblem, this.submitted);
  protected readonly password = new Field('', passwordProblem, this.submitted);

  private readonly fields = computed(() =>
    this.creating ? [this.email, this.name, this.role, this.password] : [this.email, this.name],
  );
  private readonly changed = computed(() => this.fields().some((field) => field.changed()));

  constructor() {
    askBeforeClosing(this.ref, () => this.changed(), this.confirmation);
    if (this.creating) {
      void this.loadRoles();
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.fields().some((field) => field.error() !== null)) {
      return;
    }

    this.busy.set(true);
    try {
      const email = this.email.value().trim();
      const displayName = this.name.value().trim();
      const saved = this.user
        ? await this.store.update(this.user.id, { email, displayName })
        : await this.store.create({
            email,
            displayName,
            roleId: this.role.value(),
            initialPassword: this.password.value(),
          });
      this.ref.close(saved);
    } catch (error) {
      const explained = refuseFields(error, {
        email: this.email,
        displayName: this.name,
        roleId: this.creating ? this.role : undefined,
        initialPassword: this.creating ? this.password : undefined,
      });
      if (!explained) {
        this.snackbar.error($localize`Contul nu a fost salvat. Reîncearcă.`, null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }

  private async loadRoles(): Promise<void> {
    try {
      this.roles.set(await this.store.assignableRoles());
    } catch {
      // errorInterceptor has said why. Without roles the form can't be sent, and says so.
    }
  }
}
