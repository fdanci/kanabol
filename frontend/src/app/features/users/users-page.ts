import { ComponentType } from '@angular/cdk/portal';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuContent, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
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
import { MatTooltip } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Avatar } from '../../shared/ui/avatar';
import { Chip } from '../../shared/ui/chip';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { SHEET_WIDTH } from '../../shared/ui/sheet';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { UserDto } from './models';
import { PasswordSheet } from './password-sheet';
import { RoleSheet } from './role-sheet';
import { UserSheet, UserSheetData } from './user-sheet';
import { UsersStore } from './users-store';
import { providePaginatorLabels } from '../../shared/ui/paginator-labels';

/**
 * Users (docs/UI_UX_SPEC.md §7.7): a standard table, paged by 25, 50 or 100, with what each row
 * allows in its ⋮ menu. Creating and editing happen in 600px sheets.
 */
@Component({
  selector: 'kb-users-page',
  // Its own Romanian labels, so the paginator stays out of the first bundle (docs/SPEC.md §6.2).
  providers: [providePaginatorLabels()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    PageHeader,
    EmptyState,
    Skeleton,
    Avatar,
    Chip,
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuContent,
    MatMenuItem,
    MatMenuTrigger,
    MatPaginator,
    MatTooltip,
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
  templateUrl: './users-page.html',
  styleUrl: './users-page.scss',
})
export class UsersPage {
  protected readonly store = inject(UsersStore);
  private readonly dialog = inject(MatDialog);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);
  private readonly auth = inject(AuthStore);

  protected readonly heading = $localize`Utilizatori`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Utilizatori` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca utilizatorii.`;
  protected readonly columns = ['name', 'email', 'role', 'state', 'created', 'actions'];
  protected readonly pageSizes = [25, 50, 100];
  protected readonly me = computed(() => this.auth.user()?.id ?? null);

  constructor() {
    void this.store.load(1);
  }

  protected reload(): void {
    void this.store.load();
  }

  protected turn(event: PageEvent): void {
    void this.store.load(event.pageIndex + 1, event.pageSize);
  }

  protected actionsLabel(user: UserDto): string {
    return $localize`Acțiuni pentru ${user.displayName}:name:`;
  }

  /** The lockout of docs/SPEC.md §4.5 lasts 15 minutes; an old date means it's over. */
  protected isLocked(user: UserDto): boolean {
    return user.lockedUntil !== null && Date.parse(user.lockedUntil) > Date.now();
  }

  protected async create(): Promise<void> {
    const saved = await this.sheet(UserSheet, { user: null });
    if (saved) {
      this.snackbar.show($localize`Contul lui «${saved.displayName}:name:» a fost creat.`);
    }
  }

  protected async edit(user: UserDto): Promise<void> {
    const saved = await this.sheet(UserSheet, { user });
    if (saved) {
      this.snackbar.show($localize`Contul lui «${saved.displayName}:name:» a fost salvat.`);
    }
  }

  protected async changeRole(user: UserDto): Promise<void> {
    const saved = await this.sheet(RoleSheet, { user });
    if (saved) {
      this.snackbar.show(
        $localize`«${saved.displayName}:name:» are acum rolul ${saved.roleName}:role:.`,
      );
    }
  }

  protected async resetPassword(user: UserDto): Promise<void> {
    const saved = await this.sheet(PasswordSheet, { user });
    if (saved) {
      this.snackbar.show($localize`Parola lui «${saved.displayName}:name:» a fost resetată.`);
    }
  }

  protected async deactivate(user: UserDto): Promise<void> {
    const sure = await this.confirmation.ask({
      title: $localize`Dezactivezi contul lui «${user.displayName}:name:»?`,
      message: $localize`Nu mai poate intra în cont, iar sesiunile lui se închid. Rămâne în istoric și îl poți activa oricând.`,
      confirmLabel: $localize`Dezactivează`,
      icon: 'person_off',
      destructive: true,
    });
    if (!sure) {
      return;
    }
    try {
      await this.store.deactivate(user.id);
      this.snackbar.show($localize`Contul lui «${user.displayName}:name:» a fost dezactivat.`);
    } catch {
      // errorInterceptor has said why, such as the last active Admin.
    }
  }

  protected async activate(user: UserDto): Promise<void> {
    try {
      await this.store.activate(user.id);
      this.snackbar.show($localize`Contul lui «${user.displayName}:name:» e activ din nou.`);
    } catch {
      // errorInterceptor has said why.
    }
  }

  private async sheet<T>(
    component: ComponentType<T>,
    data: UserSheetData,
  ): Promise<UserDto | undefined> {
    const ref = this.dialog.open<T, UserSheetData, UserDto>(component, {
      data,
      width: SHEET_WIDTH.form,
      ariaLabelledBy: 'kb-sheet-title',
    });
    return firstValueFrom(ref.afterClosed());
  }
}
