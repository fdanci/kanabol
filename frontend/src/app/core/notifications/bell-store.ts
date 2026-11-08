import { Injectable, Injector, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../auth/auth-store';
import { Logger } from '../logging/logger';
import { NotificationDto } from './models';
import { NotificationsApi } from './notifications-api';

/** The menu shows the last 10 (docs/UI_UX_SPEC.md §7.6). */
export const BELL_MENU_SIZE = 10;

export type BellListState = 'idle' | 'loading' | 'ready' | 'error';

/**
 * The bell in the navbar (docs/UI_UX_SPEC.md §7.6, docs/SPEC.md §9): the unread count, read once the user
 * is signed in and again at each NotificationCreated and each reconnection, and the menu's last 10, read
 * each time it opens and again at NotificationCreated while it's open. Realtime comes through a dynamic
 * import(), so SignalR stays out of the initial bundle, in the chunk it shares with the board. Signing out
 * forgets everything; a forced password change reads nothing, since the API would refuse it.
 */
@Injectable({ providedIn: 'root' })
export class BellStore {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthStore);
  private readonly logger = inject(Logger);
  private readonly injector = inject(Injector);

  private readonly unread = signal(0);
  private readonly list = signal<NotificationDto[]>([]);
  private readonly listState = signal<BellListState>('idle');
  /** Each session, so an answer meant for the last one lands nowhere. */
  private session = 0;
  private latestCount = 0;
  private latestList = 0;
  private menuOpen = false;
  private stopListening: (() => void)[] = [];

  readonly count = this.unread.asReadonly();
  readonly items = this.list.asReadonly();
  readonly state = this.listState.asReadonly();

  constructor() {
    effect(() => {
      const active = this.auth.isSignedIn() && !this.auth.mustChangePassword();
      untracked(() => {
        if (active) {
          this.start();
        } else {
          this.forget();
        }
      });
    });
  }

  /** The menu opened: its list, read now. */
  opened(): void {
    this.menuOpen = true;
    void this.readList();
  }

  closed(): void {
    this.menuOpen = false;
  }

  /** The count again, and the menu's list while it's open: something changed. */
  readAgain(): void {
    void this.readCount();
    if (this.menuOpen) {
      void this.readList();
    }
  }

  /** Asks the API, puts its answer in the menu's list, and reads the count again. */
  async markRead(notification: NotificationDto): Promise<void> {
    if (notification.readAt !== null) {
      return;
    }
    const session = this.session;
    try {
      const read = await firstValueFrom(this.api.markRead(notification.id));
      if (session === this.session) {
        this.list.update((items) => items.map((item) => (item.id === read.id ? read : item)));
      }
    } catch {
      // errorInterceptor has said why.
    }
    void this.readCount();
  }

  async markAllRead(): Promise<void> {
    try {
      await firstValueFrom(this.api.markAllRead());
    } catch {
      return;
    }
    this.readAgain();
  }

  private start(): void {
    this.session++;
    void this.readCount();
    void this.listen(this.session);
  }

  private forget(): void {
    this.session++;
    for (const stop of this.stopListening) {
      stop();
    }
    this.stopListening = [];
    this.menuOpen = false;
    this.unread.set(0);
    this.list.set([]);
    this.listState.set('idle');
  }

  private async listen(session: number): Promise<void> {
    try {
      const { Realtime } = await import('../realtime/realtime');
      if (session !== this.session) {
        return;
      }
      const realtime = this.injector.get(Realtime);
      this.stopListening.push(
        realtime.on('NotificationCreated', () => {
          this.readAgain();
        }),
        realtime.onReconnected(() => {
          this.readAgain();
        }),
      );
      await realtime.open();
    } catch (error) {
      this.logger.warn('The bell could not listen for new notifications', error);
    }
  }

  private async readCount(): Promise<void> {
    const session = this.session;
    const asked = ++this.latestCount;
    try {
      const { count } = await firstValueFrom(this.api.unreadCount());
      if (session === this.session && asked === this.latestCount) {
        this.unread.set(count);
      }
    } catch {
      // The badge keeps what it showed, until the next notification or reconnection.
    }
  }

  private async readList(): Promise<void> {
    const session = this.session;
    const asked = ++this.latestList;
    if (this.list().length === 0) {
      this.listState.set('loading');
    }
    try {
      const page = await firstValueFrom(
        this.api.list({ page: 1, pageSize: BELL_MENU_SIZE, unread: false, type: null }, true),
      );
      if (session === this.session && asked === this.latestList) {
        this.list.set(page.items);
        this.listState.set('ready');
      }
    } catch {
      if (session === this.session && asked === this.latestList) {
        this.listState.set('error');
      }
    }
  }
}
