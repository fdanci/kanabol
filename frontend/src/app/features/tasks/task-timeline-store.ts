import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskHistoryEntryDto } from './models';
import { TasksApi } from './tasks-api';

export type TaskTimelineState = 'loading' | 'ready' | 'error';

/**
 * A description's diff, by its entry: the texts once read, which an entry never changes, null until
 * they're ready.
 */
export interface DescriptionDiffState {
  state: 'loading' | 'error' | 'ready';
  before: string | null;
  after: string | null;
}

const READING: DescriptionDiffState = { state: 'loading', before: null, after: null };
const FAILED: DescriptionDiffState = { state: 'error', before: null, after: null };

/**
 * A task's history, for the timeline of its panel (docs/UI_UX_SPEC.md §7.5): the first page from the
 * newest, then the next ones at the end. Reading the same task again, after a change the panel saved,
 * keeps what it shows until the answer comes, so the timeline doesn't blink. Nothing comes from the hub
 * (docs/SPEC.md §9). A description's diff reads its entry, with the texts the list leaves out, the first
 * time it opens, and keeps it, open or not, while the timeline shows the same task (docs/SPEC.md §4.4).
 */
@Injectable()
export class TaskTimelineStore {
  private readonly api = inject(TasksApi);
  private readonly list = signal<TaskHistoryEntryDto[]>([]);
  private readonly total = signal(0);
  private readonly loadState = signal<TaskTimelineState>('loading');
  private readonly diffs = signal<ReadonlyMap<string, DescriptionDiffState>>(new Map());
  private readonly opened = signal<ReadonlySet<string>>(new Set());
  private latest = 0;
  private taskId = '';
  private page = 0;

  readonly entries = this.list.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly loadingMore = signal(false);
  /** A read of the first page is on its way, the first one or another (the tab's "Reîncarcă"). */
  readonly reading = signal(false);
  readonly hasMore = computed(() => this.list().length < this.total());

  /** The first page, from the newest. Only the last read asked for lands, however the answers arrive. */
  async load(taskId: string): Promise<void> {
    const request = ++this.latest;
    if (taskId !== this.taskId) {
      this.diffs.set(new Map());
      this.opened.set(new Set());
    }
    if (taskId !== this.taskId || this.loadState() !== 'ready') {
      this.list.set([]);
      this.total.set(0);
      this.loadState.set('loading');
    }
    this.taskId = taskId;
    this.loadingMore.set(false);
    this.reading.set(true);
    try {
      const first = await firstValueFrom(this.api.history(taskId, 1));
      if (request === this.latest) {
        this.list.set(first.items);
        this.total.set(first.totalCount);
        this.page = 1;
        this.loadState.set('ready');
      }
    } catch {
      if (request === this.latest) {
        this.loadState.set('error');
      }
    } finally {
      if (request === this.latest) {
        this.reading.set(false);
      }
    }
  }

  /**
   * The next page, at the end. A change made since the first page moves the pages by one, so a change
   * already shown can come again: it stays once.
   */
  async more(): Promise<void> {
    if (!this.hasMore() || this.loadingMore()) {
      return;
    }
    const request = this.latest;
    this.loadingMore.set(true);
    try {
      const next = await firstValueFrom(this.api.history(this.taskId, this.page + 1));
      if (request === this.latest) {
        const shown = new Set(this.list().map((entry) => entry.id));
        this.list.update((entries) => [
          ...entries,
          ...next.items.filter((entry) => !shown.has(entry.id)),
        ]);
        // A page past the end, when changes came meanwhile at the top, means there is nothing more here.
        this.total.set(next.items.length === 0 ? this.list().length : next.totalCount);
        this.page = next.page;
      }
    } catch {
      // The snackbar said why; the button stays for another try.
    } finally {
      if (request === this.latest) {
        this.loadingMore.set(false);
      }
    }
  }

  isOpen(entryId: string): boolean {
    return this.opened().has(entryId);
  }

  diff(entryId: string): DescriptionDiffState {
    return this.diffs().get(entryId) ?? READING;
  }

  /** Opens a description's diff, or closes it ("Ascunde"). Opening reads the entry once. */
  toggleDiff(entryId: string): void {
    const opened = new Set(this.opened());
    if (opened.delete(entryId)) {
      this.opened.set(opened);
      return;
    }
    this.opened.set(opened.add(entryId));
    const known = this.diffs().get(entryId);
    if (!known || known.state === 'error') {
      void this.readDiff(entryId);
    }
  }

  /** Reads the entry with its texts; an answer for a task no longer shown lands nowhere. */
  async readDiff(entryId: string): Promise<void> {
    const taskId = this.taskId;
    this.setDiff(entryId, READING);
    try {
      const entry = await firstValueFrom(this.api.historyEntry(taskId, entryId));
      if (taskId === this.taskId) {
        this.setDiff(entryId, { state: 'ready', before: entry.oldValue, after: entry.newValue });
      }
    } catch {
      if (taskId === this.taskId) {
        this.setDiff(entryId, FAILED);
      }
    }
  }

  private setDiff(entryId: string, diff: DescriptionDiffState): void {
    this.diffs.update((diffs) => new Map(diffs).set(entryId, diff));
  }
}
