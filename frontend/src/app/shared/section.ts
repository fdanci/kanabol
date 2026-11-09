import { Signal, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

export type SectionState = 'loading' | 'ready' | 'error';

/**
 * A part of a page read on its own, with its own state, as the main panel's and the reports' sections
 * (docs/UI_UX_SPEC.md §7.2, §7.8): a failure in one leaves the others as they are. It keeps what it
 * shows while it reads again, and only the last read asked for lands.
 */
export class Section<T> {
  private readonly read: () => Observable<T>;
  private latest = 0;
  private readonly data = signal<T | null>(null);
  private readonly loadState = signal<SectionState>('loading');

  readonly value: Signal<T | null> = this.data.asReadonly();
  readonly state: Signal<SectionState> = this.loadState.asReadonly();

  constructor(read: () => Observable<T>) {
    this.read = read;
  }

  async load(): Promise<void> {
    const asked = ++this.latest;
    this.loadState.set('loading');
    try {
      const value = await firstValueFrom(this.read());
      if (asked === this.latest) {
        this.data.set(value);
        this.loadState.set('ready');
      }
    } catch {
      if (asked === this.latest) {
        this.loadState.set('error');
      }
    }
  }

  update(change: (value: T) => T): void {
    const value = this.data();
    if (value !== null) {
      this.data.set(change(value));
    }
  }
}
