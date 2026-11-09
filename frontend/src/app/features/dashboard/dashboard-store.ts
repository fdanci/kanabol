import { Injectable, inject } from '@angular/core';
import { firstValueFrom, map } from 'rxjs';
import { BellStore } from '../../core/notifications/bell-store';
import { NotificationDto } from '../../core/notifications/models';
import { NotificationsApi } from '../../core/notifications/notifications-api';
import { Section } from '../../shared/section';
import { DashboardApi } from './dashboard-api';
import { DashboardProjectDto, DashboardTaskDto, MySummaryDto } from './models';
import { weekStart } from './week-start';

/** docs/UI_UX_SPEC.md §7.2: the first 8 open tasks and the last 5 notifications. */
export const DASHBOARD_TASKS = 8;
export const DASHBOARD_NOTIFICATIONS = 5;

/**
 * The main panel (docs/UI_UX_SPEC.md §7.2): read when it opens, without SignalR. A save from the task
 * panel changes the numbers and the tasks, which it reads again; the notifications and the projects stay.
 */
@Injectable()
export class DashboardStore {
  private readonly api = inject(DashboardApi);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly bell = inject(BellStore);

  readonly summary = new Section<MySummaryDto>(() => this.api.summary(weekStart(new Date())));
  readonly tasks = new Section<DashboardTaskDto[]>(() =>
    this.api.openTasks(DASHBOARD_TASKS).pipe(map((page) => page.items)),
  );
  readonly notifications = new Section<NotificationDto[]>(() =>
    this.notificationsApi
      .list({ page: 1, pageSize: DASHBOARD_NOTIFICATIONS, unread: false, type: null })
      .pipe(map((page) => page.items)),
  );
  readonly projects = new Section<DashboardProjectDto[]>(() =>
    this.api.myProjects().pipe(map((page) => page.items)),
  );

  loadAll(): void {
    void this.summary.load();
    void this.tasks.load();
    void this.notifications.load();
    void this.projects.load();
  }

  readAfterSave(): void {
    void this.summary.load();
    void this.tasks.load();
  }

  /** The row takes the API's answer, read, and the bell reads its count again; one read before isn't asked again. */
  async markRead(notification: NotificationDto): Promise<void> {
    if (notification.readAt !== null) {
      return;
    }
    try {
      const read = await firstValueFrom(this.notificationsApi.markRead(notification.id));
      this.notifications.update((rows) => rows.map((row) => (row.id === read.id ? read : row)));
    } catch {
      // errorInterceptor has said why.
    }
    this.bell.readAgain();
  }
}
