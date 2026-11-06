import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
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
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { HasPermission } from '../../shared/directives/has-permission';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { Avatar } from '../../shared/ui/avatar';
import { Chip } from '../../shared/ui/chip';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { SHEET_WIDTH } from '../../shared/ui/sheet';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { ProjectDto, ProjectSummaryDto } from './models';
import { ProjectSheet } from './project-sheet';
import { ProjectsStore } from './projects-store';

/**
 * The projects the user can open (docs/UI_UX_SPEC.md §7.3), as a table: name, owner, members as
 * overlapping avatars, open and overdue tasks, and the last change. A row opens the project's board.
 */
@Component({
  selector: 'kb-projects-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    RelativeTimePipe,
    PageHeader,
    EmptyState,
    Skeleton,
    Avatar,
    Chip,
    HasPermission,
    MatButton,
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
  templateUrl: './projects-page.html',
  styleUrl: './projects-page.scss',
})
export class ProjectsPage {
  protected readonly store = inject(ProjectsStore);
  private readonly dialog = inject(MatDialog);
  private readonly snackbar = inject(Snackbar);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthStore);

  protected readonly permissions = PERMISSIONS;
  protected readonly heading = $localize`Proiecte`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Proiecte` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca proiectele.`;
  protected readonly emptyTitle = $localize`Niciun proiect încă`;
  protected readonly columns = ['name', 'owners', 'members', 'open', 'overdue', 'updated'];
  protected readonly pageSizes = [25, 50, 100];
  protected readonly fullDate = fullDate;

  constructor() {
    void this.store.load(1);
  }

  protected canCreate(): boolean {
    return this.auth.has(PERMISSIONS.projectsCreate);
  }

  protected reload(): void {
    void this.store.load();
  }

  protected turn(event: PageEvent): void {
    void this.store.load(event.pageIndex + 1, event.pageSize);
  }

  protected toggleArchived(): void {
    void this.store.showArchived(!this.store.includeArchived());
  }

  protected ownerNames(project: ProjectSummaryDto): string {
    return project.owners.map((owner) => owner.displayName).join(', ');
  }

  /** The members beyond the four avatars. */
  protected more(project: ProjectSummaryDto): number {
    return project.memberCount - project.memberPreview.length;
  }

  protected membersLabel(project: ProjectSummaryDto): string {
    return $localize`${project.memberCount}:count: membri`;
  }

  /** A click anywhere on the row opens the board, as the link in its name does from the keyboard. */
  protected open(project: ProjectSummaryDto, event: MouseEvent): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) {
      void this.router.navigate(['/projects', project.id, 'board']);
    }
  }

  protected async create(): Promise<void> {
    const ref = this.dialog.open<ProjectSheet, undefined, ProjectDto>(ProjectSheet, {
      width: SHEET_WIDTH.form,
      ariaLabelledBy: 'kb-sheet-title',
    });
    const saved = await firstValueFrom(ref.afterClosed());
    if (saved) {
      this.snackbar.show($localize`Proiectul «${saved.name}:name:» a fost creat.`);
      void this.router.navigate(['/projects', saved.id]);
    }
  }
}
