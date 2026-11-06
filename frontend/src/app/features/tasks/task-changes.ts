import { Injectable, signal } from '@angular/core';

/**
 * Counts the changes the panel saved, so a list behind it reads its page again. SignalR brings the
 * changes of others in group 14; this is only for the ones made here.
 */
@Injectable({ providedIn: 'root' })
export class TaskChanges {
  private readonly count = signal(0);

  readonly version = this.count.asReadonly();

  saved(): void {
    this.count.update((count) => count + 1);
  }
}
