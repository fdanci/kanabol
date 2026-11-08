import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { NotificationDto } from '../../core/notifications/models';
import { NotificationLine } from '../../core/notifications/notification-line';
import { priorityLabel } from '../../shared/models/task-filters';
import { TaskChanges } from '../../shared/task-changes';
import { openTaskPanel } from '../../shared/task-panel';
import { Chip, ChipKind } from '../../shared/ui/chip';
import { DeadlineChip } from '../../shared/ui/deadline-chip';
import { EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatTile } from '../../shared/ui/stat-tile';
import { DashboardStore } from './dashboard-store';
import { DashboardProjectDto, DashboardTaskDto } from './models';

/**
 * The main panel at / (docs/UI_UX_SPEC.md §7.2): four stat tiles, "Task-urile mele", "Notificări recente"
 * and "Proiectele mele", each read on its own with its skeleton and its error. It reads when it opens,
 * without SignalR, and reads the tiles and the tasks again after a save from the task panel.
 */
@Component({
  selector: 'kb-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DashboardStore],
  imports: [
    MatButton,
    MatTableModule,
    RouterLink,
    Chip,
    DeadlineChip,
    EmptyState,
    PageHeader,
    Skeleton,
    StatTile,
    NotificationLine,
  ],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage {
  protected readonly store = inject(DashboardStore);
  private readonly router = inject(Router);
  private readonly changes = inject(TaskChanges);

  protected readonly heading = $localize`Panou principal`;
  protected readonly summaryFailed = $localize`Nu am putut încărca sumarul.`;
  protected readonly tasksFailed = $localize`Nu am putut încărca task-urile.`;
  protected readonly notificationsFailed = $localize`Nu am putut încărca notificările.`;
  protected readonly projectsFailed = $localize`Nu am putut încărca proiectele.`;
  protected readonly noTasks = $localize`Niciun task deschis.`;
  protected readonly noNotifications = $localize`Nicio notificare.`;
  protected readonly noProjects = $localize`Nu ești în niciun proiect încă.`;
  protected readonly columns = ['key', 'title', 'project', 'dueDate', 'priority'];
  protected readonly overdueQuery = { overdue: 1 };

  protected readonly summary = computed(() => this.store.summary.value());

  constructor() {
    this.store.loadAll();

    // A save from the task panel: the numbers and the tasks may have changed. Not the first value.
    let seen = this.changes.version();
    effect(() => {
      const version = this.changes.version();
      if (version !== seen) {
        seen = version;
        untracked(() => {
          this.store.readAfterSave();
        });
      }
    });
  }

  protected priorityLabel(task: DashboardTaskDto): string {
    return priorityLabel(task.priority);
  }

  protected priorityKind(task: DashboardTaskDto): ChipKind {
    return task.priority.toLowerCase() as ChipKind;
  }

  protected done(task: DashboardTaskDto): boolean {
    return task.columnIsDone || task.completedAt !== null;
  }

  protected openTask(task: DashboardTaskDto): void {
    void openTaskPanel(this.router, task.key);
  }

  /** Marks it read and opens its task over the panel, as a task row does. */
  protected openNotification(notification: NotificationDto): void {
    void this.store.markRead(notification);
    if (notification.task) {
      void openTaskPanel(this.router, notification.task.key);
    }
  }

  protected owners(project: DashboardProjectDto): string {
    return project.owners.map((owner) => owner.displayName).join(', ');
  }

  /** Finished of open and finished, as the bar's width: 0 for a project without tasks. */
  protected progress(project: DashboardProjectDto): number {
    const total = project.openTaskCount + project.completedTaskCount;
    return total === 0 ? 0 : Math.round((project.completedTaskCount / total) * 100);
  }

  protected total(project: DashboardProjectDto): number {
    return project.openTaskCount + project.completedTaskCount;
  }
}
