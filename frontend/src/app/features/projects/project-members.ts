import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuContent, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import {
  MatCell,
  MatCellDef,
  MatColumnDef,
  MatHeaderCell,
  MatHeaderCellDef,
  MatHeaderRow,
  MatHeaderRowDef,
  MatRow,
  MatRowDef,
  MatTable,
} from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Avatar } from '../../shared/ui/avatar';
import { Chip } from '../../shared/ui/chip';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { SHEET_WIDTH } from '../../shared/ui/sheet';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { MemberSheet } from './member-sheet';
import { ProjectMemberDto, ProjectRole } from './models';
import { ProjectStore } from './project-store';

/**
 * The members of a project (docs/UI_UX_SPEC.md §7.3): who they are, their role and whether their
 * account is active. Those who can change the project add members, change their role and remove
 * them; the last owner stays, so its menu has neither.
 */
@Component({
  selector: 'kb-project-members',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    Avatar,
    Chip,
    EmptyState,
    Skeleton,
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuContent,
    MatMenuItem,
    MatMenuTrigger,
    MatTable,
    MatColumnDef,
    MatHeaderCell,
    MatHeaderCellDef,
    MatCell,
    MatCellDef,
    MatHeaderRow,
    MatHeaderRowDef,
    MatRow,
    MatRowDef,
  ],
  templateUrl: './project-members.html',
  styleUrl: './project-members.scss',
})
export class ProjectMembers {
  protected readonly store = inject(ProjectStore);
  private readonly dialog = inject(MatDialog);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);
  private readonly auth = inject(AuthStore);

  protected readonly loadFailed = $localize`Nu am putut încărca membrii.`;
  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  protected readonly columns = computed(() =>
    this.store.canChange()
      ? ['name', 'email', 'role', 'state', 'actions']
      : ['name', 'email', 'role', 'state'],
  );

  constructor() {
    void this.store.loadMembers();
  }

  protected reload(): void {
    void this.store.loadMembers();
  }

  protected actionsLabel(member: ProjectMemberDto): string {
    return $localize`Acțiuni pentru ${member.displayName}:name:`;
  }

  /** The last owner can neither stop being one nor leave: the API refuses both. */
  protected isLastOwner(member: ProjectMemberDto): boolean {
    return member.role === 'Owner' && this.store.ownerCount() === 1;
  }

  protected async add(): Promise<void> {
    const ref = this.dialog.open<MemberSheet, ProjectStore, ProjectMemberDto>(MemberSheet, {
      data: this.store,
      width: SHEET_WIDTH.form,
      ariaLabelledBy: 'kb-sheet-title',
    });
    const added = await firstValueFrom(ref.afterClosed());
    if (added) {
      this.snackbar.show($localize`«${added.displayName}:name:» a fost adăugat în proiect.`);
    }
  }

  protected async changeRole(member: ProjectMemberDto, role: ProjectRole): Promise<void> {
    try {
      await this.store.changeMemberRole(member.userId, role);
      this.snackbar.show(
        role === 'Owner'
          ? $localize`«${member.displayName}:name:» e acum owner.`
          : $localize`«${member.displayName}:name:» e acum membru.`,
      );
    } catch {
      // errorInterceptor has said why, such as the last owner.
    }
  }

  protected async remove(member: ProjectMemberDto): Promise<void> {
    const project = this.store.project()?.name ?? '';
    const leaving = member.userId === this.me();
    const sure = await this.confirmation.ask(
      leaving
        ? {
            title: $localize`Ieși din proiectul «${project}:project:»?`,
            message: $localize`Nu-l mai vezi până nu te adaugă din nou un owner.`,
            confirmLabel: $localize`Ieși`,
            icon: 'logout',
            destructive: true,
          }
        : {
            title: $localize`Scoți «${member.displayName}:name:» din proiect?`,
            message: $localize`Nu mai vede proiectul. Îl poți adăuga din nou oricând.`,
            confirmLabel: $localize`Scoate`,
            icon: 'person_remove',
            destructive: true,
          },
    );
    if (!sure) {
      return;
    }
    try {
      await this.store.removeMember(member.userId);
      this.snackbar.show(
        leaving
          ? $localize`Ai ieșit din proiectul «${project}:project:».`
          : $localize`«${member.displayName}:name:» a fost scos din proiect.`,
      );
    } catch {
      // errorInterceptor has said why.
    }
  }
}
