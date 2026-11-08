import { Provider, signal } from '@angular/core';
import { BoardEvent, BoardEvents } from './models';
import { Realtime } from './realtime';

/**
 * Realtime for the specs of whoever listens: what they joined and left, and events and reconnections
 * the spec sends them. A plain file, so importing it runs no spec.
 */
export class FakeRealtime {
  readonly connectionId = signal<string | null>('my-tab');
  readonly joined: string[] = [];
  readonly left: string[] = [];
  /** How many times the bell opened the connection. */
  opened = 0;
  private readonly listeners = new Map<string, Set<(payload: unknown) => void>>();
  private readonly reconnectionListeners = new Set<() => void>();

  open(): Promise<void> {
    this.opened++;
    return Promise.resolve();
  }

  join(projectId: string): Promise<void> {
    this.joined.push(projectId);
    return Promise.resolve();
  }

  leave(projectId: string): Promise<void> {
    this.left.push(projectId);
    return Promise.resolve();
  }

  on<E extends BoardEvent>(event: E, handler: (payload: BoardEvents[E]) => void): () => void {
    const handlers = this.listeners.get(event) ?? new Set();
    this.listeners.set(event, handlers);
    const added = handler as (payload: unknown) => void;
    handlers.add(added);
    return () => {
      handlers.delete(added);
    };
  }

  onReconnected(handler: () => void): () => void {
    this.reconnectionListeners.add(handler);
    return () => {
      this.reconnectionListeners.delete(handler);
    };
  }

  send(event: BoardEvent, payload: unknown): void {
    for (const handler of this.listeners.get(event) ?? []) {
      handler(payload);
    }
  }

  reconnect(): void {
    for (const handler of this.reconnectionListeners) {
      handler();
    }
  }

  /** How many are listening to that event: none once they're gone. */
  listening(event: BoardEvent): number {
    return this.listeners.get(event)?.size ?? 0;
  }
}

export function provideFakeRealtime(fake = new FakeRealtime()): Provider {
  return { provide: Realtime, useValue: fake };
}
