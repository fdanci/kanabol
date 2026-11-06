import { Injectable, signal } from '@angular/core';

/**
 * Counts the changes the panel saved, so the list or the board behind it reads its tasks again.
 * SignalR brings the changes of others in group 14; this is only for the ones made here. It lives
 * in shared/ because the panel is the tasks feature's and the board is not (docs/SPEC.md §6.1).
 */
@Injectable({ providedIn: 'root' })
export class TaskChanges {
  private readonly count = signal(0);

  readonly version = this.count.asReadonly();

  saved(): void {
    this.count.update((count) => count + 1);
  }
}
