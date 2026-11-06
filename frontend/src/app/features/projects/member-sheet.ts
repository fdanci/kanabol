import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocomplete, MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MatButton } from '@angular/material/button';
import { MatOption } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSelect } from '@angular/material/select';
import { catchError, debounceTime, from, of, switchMap } from 'rxjs';
import { Avatar } from '../../shared/ui/avatar';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { Field } from '../../shared/ui/field';
import { MemberCandidateDto, ProjectMemberDto, ProjectRole } from './models';
import { ProjectStore } from './project-store';

/** How long the search waits for the typing to stop. */
export const SEARCH_DELAY_MS = 250;

function userProblem(value: string): string | null {
  return value === '' ? $localize`Alege un utilizator din listă.` : null;
}

function roleProblem(value: string): string | null {
  return value === 'Owner' || value === 'Member' ? null : $localize`Alege un rol.`;
}

/**
 * A new member, in a 600px sheet: an active user who isn't in the project yet, found by name or
 * email, and their role. It closes with the member as the API saved them.
 */
@Component({
  selector: 'kb-member-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatAutocomplete, MatAutocompleteTrigger, MatOption, MatSelect, Avatar],
  templateUrl: './member-sheet.html',
  styleUrl: './sheet.scss',
})
export class MemberSheet {
  private readonly store = inject<ProjectStore>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<MemberSheet, ProjectMemberDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);

  protected readonly busy = signal(false);
  private readonly submitted = signal(false);
  protected readonly search = signal('');
  protected readonly chosen = signal<MemberCandidateDto | null>(null);
  protected readonly user = new Field('', userProblem, this.submitted);
  protected readonly role = new Field('Member', roleProblem, this.submitted);

  /** The candidates for what was typed, read once the typing stops. A failed search finds nobody. */
  protected readonly candidates = toSignal(
    toObservable(this.search).pipe(
      debounceTime(SEARCH_DELAY_MS),
      switchMap((term) => from(this.store.candidates(term)).pipe(catchError(() => of([])))),
    ),
    { initialValue: [] as MemberCandidateDto[] },
  );

  private readonly changed = computed(() => this.search() !== '' || this.role.changed());

  constructor() {
    askBeforeClosing(this.ref, () => this.changed(), this.confirmation);
  }

  protected readonly display = (candidate: MemberCandidateDto | string | null): string =>
    typeof candidate === 'string' ? candidate : (candidate?.displayName ?? '');

  /** Typing again lets go of the user chosen before. */
  protected typed(value: string): void {
    this.search.set(value);
    if (this.chosen() !== null) {
      this.chosen.set(null);
      this.user.value.set('');
    }
  }

  protected choose(candidate: MemberCandidateDto): void {
    this.chosen.set(candidate);
    this.user.value.set(candidate.id);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.user.error() !== null || this.role.error() !== null) {
      return;
    }

    this.busy.set(true);
    try {
      this.ref.close(
        await this.store.addMember(this.user.value(), this.role.value() as ProjectRole),
      );
    } catch {
      // errorInterceptor has said why: someone added them meanwhile, or the account was deactivated.
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }
}
