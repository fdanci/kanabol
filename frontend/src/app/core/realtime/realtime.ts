import { Injectable, InjectionToken, effect, inject, untracked } from '@angular/core';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthStore } from '../auth/auth-store';
import { Logger } from '../logging/logger';
import { HubLog } from './hub-log';
import { BoardEvent, BoardEvents } from './models';
import { TabConnection } from './tab-connection';

/** The SignalR connection, as far as Realtime uses it, so the specs can stand in for it. */
export interface HubLink {
  readonly connectionId: string | null;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(method: string, ...args: unknown[]): Promise<unknown>;
  on(event: string, handler: (payload: unknown) => void): void;
  onclose(callback: (error?: Error) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
}

/** SignalR's own reconnection: at once, then after 2, 10 and 30 seconds. */
const RECONNECT_DELAYS = [0, 2_000, 10_000, 30_000];

/** After SignalR gives up, or couldn't start: again after 1 and 5 seconds, then every 30. */
const RESTART_DELAYS = [1_000, 5_000, 30_000];

/** The board hub, with the token in its address (docs/SPEC.md §4.5), and SignalR's log in ours. */
export const HUB_LINK_FACTORY = new InjectionToken<(accessToken: () => Promise<string>) => HubLink>(
  'HUB_LINK_FACTORY',
  {
    providedIn: 'root',
    factory: () => {
      const log = new HubLog(inject(Logger));
      return (accessToken) =>
        new HubConnectionBuilder()
          .withUrl(`${environment.hubsBaseUrl}/board`, { accessTokenFactory: accessToken })
          .withAutomaticReconnect(RECONNECT_DELAYS)
          .configureLogging(log)
          .build();
    },
  },
);

/**
 * The one connection to /hubs/board (docs/SPEC.md §9). It opens when the first board joins, and stays
 * while the user is signed in, so the API also knows the user's own group; it closes with the session.
 * SignalR reconnects by itself, and when it gives up this starts it again; after either, it joins the
 * projects again and tells its listeners, who read what they missed. The connection's id goes in
 * X-Connection-Id on every API request (connectionInterceptor), so a board can tell its own changes.
 */
@Injectable({ providedIn: 'root' })
export class Realtime {
  private readonly auth = inject(AuthStore);
  private readonly logger = inject(Logger);
  private readonly create = inject(HUB_LINK_FACTORY);
  private readonly id = inject(TabConnection).id;
  private readonly listeners = new Map<string, Set<(payload: unknown) => void>>();
  private readonly reconnectionListeners = new Set<() => void>();
  private readonly projects = new Set<string>();
  private link: HubLink | null = null;
  private starting: Promise<boolean> | null = null;
  private connectedBefore = false;
  private restarts = 0;
  private restartTimer: ReturnType<typeof setTimeout> | null = null;

  /** Null while there's no connection, or while it reconnects. */
  readonly connectionId = this.id.asReadonly();

  constructor() {
    effect(() => {
      if (!this.auth.isSignedIn()) {
        untracked(() => {
          this.disconnect();
        });
      }
    });
  }

  /** The project's changes, for a board; connects first if it has to. */
  async join(projectId: string): Promise<void> {
    this.projects.add(projectId);
    if (this.link && this.id() !== null) {
      await this.invokeJoin(projectId);
    } else {
      await this.connect();
    }
  }

  async leave(projectId: string): Promise<void> {
    this.projects.delete(projectId);
    if (!this.link || this.id() === null) {
      return;
    }
    try {
      await this.link.invoke('LeaveProject', projectId);
    } catch (error) {
      this.logger.debug('The board could not leave its project on the hub', error);
    }
  }

  /** The handler hears each event of that name, until the function returned is called. */
  on<E extends BoardEvent>(event: E, handler: (payload: BoardEvents[E]) => void): () => void {
    let handlers = this.listeners.get(event);
    if (!handlers) {
      handlers = new Set();
      this.listeners.set(event, handlers);
      this.link?.on(event, (payload) => {
        this.dispatch(event, payload);
      });
    }
    const added = handler as (payload: unknown) => void;
    handlers.add(added);
    return () => {
      handlers.delete(added);
    };
  }

  /** After a reconnection, when whatever happened meanwhile went unheard. */
  onReconnected(handler: () => void): () => void {
    this.reconnectionListeners.add(handler);
    return () => {
      this.reconnectionListeners.delete(handler);
    };
  }

  private connect(): Promise<boolean> {
    this.starting ??= this.start().finally(() => {
      this.starting = null;
    });
    return this.starting;
  }

  private async start(): Promise<boolean> {
    if (!this.auth.isSignedIn()) {
      return false;
    }
    const link = this.link ?? this.build();
    try {
      await link.start();
    } catch (error) {
      this.logger.warn('The board could not connect for the changes of others', error);
      this.restartLater(link);
      return false;
    }
    if (this.link !== link) {
      // Signed out while it connected.
      void link.stop();
      return false;
    }
    this.restarts = 0;
    this.id.set(link.connectionId);
    await this.joinAll(link);
    if (this.connectedBefore) {
      this.reconnected();
    }
    this.connectedBefore = true;
    return true;
  }

  private build(): HubLink {
    const link = this.create(async () => (await this.auth.freshToken()) ?? '');
    for (const event of this.listeners.keys()) {
      link.on(event, (payload) => {
        this.dispatch(event, payload);
      });
    }
    link.onreconnecting(() => {
      this.id.set(null);
    });
    link.onreconnected(() => {
      this.id.set(link.connectionId);
      void this.joinAll(link).then(() => {
        this.reconnected();
      });
    });
    link.onclose(() => {
      this.id.set(null);
      this.restartLater(link);
    });
    this.link = link;
    return link;
  }

  private restartLater(link: HubLink): void {
    if (this.link !== link || this.restartTimer !== null || !this.auth.isSignedIn()) {
      return;
    }
    const delay = RESTART_DELAYS[Math.min(this.restarts, RESTART_DELAYS.length - 1)];
    this.restarts++;
    this.restartTimer = setTimeout(() => {
      this.restartTimer = null;
      void this.connect();
    }, delay);
  }

  private disconnect(): void {
    if (this.restartTimer !== null) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }
    const link = this.link;
    this.link = null;
    this.id.set(null);
    this.projects.clear();
    this.connectedBefore = false;
    this.restarts = 0;
    void link?.stop();
  }

  private async joinAll(link: HubLink): Promise<void> {
    await Promise.all([...this.projects].map((projectId) => this.invokeJoin(projectId, link)));
  }

  private async invokeJoin(projectId: string, link = this.link): Promise<void> {
    try {
      await link?.invoke('JoinProject', projectId);
    } catch (error) {
      // Refused, such as for a project the user no longer sees: the board says so when it reads itself.
      this.logger.warn('The board could not join its project on the hub', error);
    }
  }

  private dispatch(event: string, payload: unknown): void {
    for (const handler of this.listeners.get(event) ?? []) {
      handler(payload);
    }
  }

  private reconnected(): void {
    for (const handler of this.reconnectionListeners) {
      handler();
    }
  }
}
