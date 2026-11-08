import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BellStore } from '../../core/notifications/bell-store';
import { NotificationDto, NotificationQuery } from '../../core/notifications/models';
import { NotificationsApi } from '../../core/notifications/notifications-api';

export type NotificationsState = 'loading' | 'ready' | 'error';

/**
 * The notifications page (docs/UI_UX_SPEC.md §7.6): a page of the user's own, as the address asks for it.
 * What it marks read it tells the bell, which reads its count again.
 */
@Injectable()
export class NotificationsStore {
  private readonly api = inject(NotificationsApi);
  private readonly bell = inject(BellStore);
  private latest = 0;
  private lastQuery: NotificationQuery | null = null;

  private readonly rowList = signal<NotificationDto[]>([]);
  private readonly total = signal(0);
  private readonly loadState = signal<NotificationsState>('loading');

  readonly rows = this.rowList.asReadonly();
  readonly totalCount = this.total.asReadonly();
  readonly state = this.loadState.asReadonly();

  /** Only the last page asked for lands, however the answers arrive. */
  async load(query: NotificationQuery): Promise<void> {
    this.lastQuery = query;
    const asked = ++this.latest;
    this.loadState.set('loading');
    try {
      const page = await firstValueFrom(this.api.list(query));
      if (asked === this.latest) {
        this.rowList.set(page.items);
        this.total.set(page.totalCount);
        this.loadState.set('ready');
      }
    } catch {
      if (asked === this.latest) {
        this.loadState.set('error');
      }
    }
  }

  reload(): Promise<void> {
    return this.lastQuery ? this.load(this.lastQuery) : Promise.resolve();
  }

  /** The row takes the API's answer, read; one read before isn't asked again. */
  async markRead(notification: NotificationDto): Promise<void> {
    if (notification.readAt !== null) {
      return;
    }
    try {
      const read = await firstValueFrom(this.api.markRead(notification.id));
      this.rowList.update((rows) => rows.map((row) => (row.id === read.id ? read : row)));
    } catch {
      // errorInterceptor has said why.
    }
    this.bell.readAgain();
  }

  async markAllRead(): Promise<void> {
    try {
      await firstValueFrom(this.api.markAllRead());
    } catch {
      return;
    }
    this.bell.readAgain();
    await this.reload();
  }
}
