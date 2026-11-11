import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskMessage } from '../../core/realtime/models';
import { Realtime } from '../../core/realtime/realtime';
import { BoardApi } from './board-api';
import { ArchivedTaskDto } from './models';

/** Where the column's state is kept, open or closed, in this browser (docs/UI_UX_SPEC.md §7.4). */
export const ARCHIVED_STATE_KEY = 'kb.board.archived';

type ArchivedState = 'idle' | 'loading' | 'ready' | 'error';

function wasOpen(): boolean {
  try {
    return localStorage.getItem(ARCHIVED_STATE_KEY) === 'open';
  } catch {
    return false;
  }
}

/**
 * The column Arhivate of a board (docs/SPEC.md §4.4, docs/UI_UX_SPEC.md §7.4), for tasks.archive: the
 * project's archived cards, from the latest archiving, 50 at a time, read again after an archiving or a
 * restoring, this tab's or anyone's. Closed by default, and as this browser last left it. From group 23 it
 * reads only while open, each time it opens: closed, its band counts with the board's archivedCount. The
 * board page provides one with its BoardStore.
 */
@Injectable()
export class ArchivedStore {
  private readonly api = inject(BoardApi);
  private projectId: string | null = null;
  private pages = 1;
  /** Each read counts, so an answer that came after a newer read lands nowhere. */
  private reads = 0;

  readonly cards = signal<ArchivedTaskDto[]>([]);
  readonly total = signal(0);
  readonly state = signal<ArchivedState>('idle');
  readonly loadingMore = signal(false);
  readonly open = signal(wasOpen());
  readonly hasMore = computed(() => this.cards().length < this.total());

  constructor() {
    const realtime = inject(Realtime);
    const heard = (message: TaskMessage) => {
      if (message.projectId === this.projectId) {
        void this.read();
      }
    };
    const stopListening = [
      realtime.on('TaskArchived', heard),
      realtime.on('TaskRestored', heard),
      // Whatever was archived or restored while the connection was away went unheard.
      realtime.onReconnected(() => void this.read()),
    ];
    inject(DestroyRef).onDestroy(() => {
      for (const stop of stopListening) {
        stop();
      }
    });
  }

  /** The project's archived cards, read for each project once the column is open. */
  show(projectId: string): void {
    if (this.projectId === projectId) {
      return;
    }
    this.projectId = projectId;
    this.cards.set([]);
    this.total.set(0);
    this.state.set('idle');
    void this.read();
  }

  toggle(): void {
    this.open.update((open) => !open);
    try {
      localStorage.setItem(ARCHIVED_STATE_KEY, this.open() ? 'open' : 'closed');
    } catch {
      // A browser that keeps nothing opens the column closed next time.
    }
    void this.read();
  }

  /** The first page again, keeping what's shown until it comes; nothing while the column is closed. */
  async read(): Promise<void> {
    const projectId = this.projectId;
    if (projectId === null || !this.open()) {
      return;
    }
    const read = ++this.reads;
    if (this.state() !== 'ready') {
      this.state.set('loading');
    }
    try {
      const page = await firstValueFrom(this.api.archived(projectId, 1));
      if (read === this.reads) {
        this.pages = 1;
        this.cards.set(page.items);
        this.total.set(page.totalCount);
        this.state.set('ready');
      }
    } catch {
      if (read === this.reads && this.state() !== 'ready') {
        this.state.set('error');
      }
    }
  }

  /** The next 50, after those shown, each card once. */
  async more(): Promise<void> {
    const projectId = this.projectId;
    if (projectId === null || this.loadingMore()) {
      return;
    }
    const read = this.reads;
    this.loadingMore.set(true);
    try {
      const page = await firstValueFrom(this.api.archived(projectId, this.pages + 1));
      if (read === this.reads) {
        this.pages++;
        const shown = new Set(this.cards().map((card) => card.id));
        this.cards.update((cards) => [
          ...cards,
          ...page.items.filter((card) => !shown.has(card.id)),
        ]);
        this.total.set(page.totalCount);
      }
    } catch {
      // The interceptor said why; the button stays for another try.
    } finally {
      this.loadingMore.set(false);
    }
  }

  /** True once the API archived it; the column reads itself again. */
  async archive(taskId: string): Promise<boolean> {
    try {
      await firstValueFrom(this.api.archive(taskId));
      void this.read();
      return true;
    } catch {
      return false;
    }
  }

  /** True once the API restored it into its column; the column reads itself again either way. */
  async restore(card: ArchivedTaskDto): Promise<boolean> {
    try {
      await firstValueFrom(this.api.restore(card.id));
      return true;
    } catch {
      return false;
    } finally {
      void this.read();
    }
  }
}
