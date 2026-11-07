import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthStore } from '../auth/auth-store';
import { Logger } from '../logging/logger';
import { HUB_LINK_FACTORY, HubLink, Realtime } from './realtime';

/** The SignalR connection, as far as Realtime uses it: what it was asked, and what the specs make it do. */
class FakeHubLink implements HubLink {
  connectionId: string | null = null;
  starts = 0;
  readonly invoked: unknown[][] = [];
  readonly stop = vi.fn(() => {
    this.connectionId = null;
    return Promise.resolve();
  });
  failNextStart = false;
  private readonly handlers = new Map<string, ((payload: unknown) => void)[]>();
  private closed?: (error?: Error) => void;
  private reconnecting?: (error?: Error) => void;
  private reconnected?: (connectionId?: string) => void;

  constructor(readonly accessToken: () => Promise<string>) {}

  async start(): Promise<void> {
    this.starts++;
    await this.accessToken();
    if (this.failNextStart) {
      this.failNextStart = false;
      throw new Error('Failed to complete negotiation with the server');
    }
    this.connectionId = `conn-${String(this.starts)}`;
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invoked.push([method, ...args]);
    return Promise.resolve();
  }

  on(event: string, handler: (payload: unknown) => void): void {
    this.handlers.set(event, [...(this.handlers.get(event) ?? []), handler]);
  }

  onclose(callback: (error?: Error) => void): void {
    this.closed = callback;
  }

  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnecting = callback;
  }

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnected = callback;
  }

  send(event: string, payload: unknown): void {
    for (const handler of this.handlers.get(event) ?? []) {
      handler(payload);
    }
  }

  /** SignalR lost the connection, and is looking for it. */
  drop(): void {
    this.connectionId = null;
    this.reconnecting?.(new Error('WebSocket closed'));
  }

  /** SignalR lost the connection and found it again, under a new id. */
  dropAndReconnect(connectionId: string): void {
    this.drop();
    this.connectionId = connectionId;
    this.reconnected?.(connectionId);
  }

  /** SignalR gave up reconnecting, or was told not to. */
  close(): void {
    this.connectionId = null;
    this.closed?.(new Error('Server timeout elapsed'));
  }
}

/** Lets pending promises run: a start, the joins after it. */
async function flush(): Promise<void> {
  for (let round = 0; round < 5; round++) {
    await Promise.resolve();
  }
}

/** docs/SPEC.md §9: one connection to /hubs/board, with automatic reconnection and joins again. */
describe('Realtime', () => {
  const signedIn = signal(true);
  let freshToken: ReturnType<typeof vi.fn>;
  let links: FakeHubLink[];
  let logger: { debug: ReturnType<typeof vi.fn>; warn: ReturnType<typeof vi.fn> };
  let realtime: Realtime;

  function link(): FakeHubLink {
    expect(links).toHaveLength(1);
    return links[0];
  }

  beforeEach(() => {
    signedIn.set(true);
    freshToken = vi.fn(() => Promise.resolve('fresh-token'));
    links = [];
    logger = { debug: vi.fn(), warn: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { isSignedIn: signedIn, freshToken } },
        { provide: Logger, useValue: logger },
        {
          provide: HUB_LINK_FACTORY,
          useValue: (accessToken: () => Promise<string>) => {
            const created = new FakeHubLink(accessToken);
            links.push(created);
            return created;
          },
        },
      ],
    });
    realtime = TestBed.inject(Realtime);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('connects only once a board joins, with a fresh token, and joins its project', async () => {
    expect(links).toHaveLength(0);
    expect(realtime.connectionId()).toBeNull();

    await realtime.join('p1');

    expect(link().starts).toBe(1);
    expect(freshToken).toHaveBeenCalled();
    expect(link().invoked).toEqual([['JoinProject', 'p1']]);
    expect(realtime.connectionId()).toBe('conn-1');
  });

  it('joins the next project on the same connection', async () => {
    await realtime.join('p1');
    await realtime.join('p2');

    expect(link().starts).toBe(1);
    expect(link().invoked).toEqual([
      ['JoinProject', 'p1'],
      ['JoinProject', 'p2'],
    ]);
  });

  it('joins once for two boards that ask while it connects', async () => {
    await Promise.all([realtime.join('p1'), realtime.join('p2')]);

    expect(link().starts).toBe(1);
    expect(link().invoked).toEqual([
      ['JoinProject', 'p1'],
      ['JoinProject', 'p2'],
    ]);
  });

  it('hands each event to whoever listens, until they stop listening', async () => {
    const heard: unknown[] = [];
    const stop = realtime.on('TaskMoved', (payload) => heard.push(payload));
    await realtime.join('p1');

    link().send('TaskMoved', { taskId: 't1' });
    stop();
    link().send('TaskMoved', { taskId: 't2' });

    expect(heard).toEqual([{ taskId: 't1' }]);
  });

  it('hands events also to whoever starts listening once it is connected', async () => {
    await realtime.join('p1');
    const heard: unknown[] = [];

    realtime.on('TaskUpdated', (payload) => heard.push(payload));
    link().send('TaskUpdated', { field: 'Title' });

    expect(heard).toEqual([{ field: 'Title' }]);
  });

  it('leaves a project, and joins only the others after reconnecting, under the new id', async () => {
    const reconnections = vi.fn();
    realtime.onReconnected(reconnections);
    await realtime.join('p1');
    await realtime.join('p2');

    await realtime.leave('p1');
    link().dropAndReconnect('conn-again');
    await flush();

    expect(link().invoked).toEqual([
      ['JoinProject', 'p1'],
      ['JoinProject', 'p2'],
      ['LeaveProject', 'p1'],
      ['JoinProject', 'p2'],
    ]);
    expect(realtime.connectionId()).toBe('conn-again');
    expect(reconnections).toHaveBeenCalledTimes(1);
  });

  it('has no connection id while it reconnects, so requests go without one', async () => {
    await realtime.join('p1');

    link().drop();

    expect(realtime.connectionId()).toBeNull();
  });

  it('starts again after the connection closed for good, joins again and says so', async () => {
    vi.useFakeTimers();
    const reconnections = vi.fn();
    realtime.onReconnected(reconnections);
    await realtime.join('p1');

    link().close();
    expect(realtime.connectionId()).toBeNull();
    await vi.advanceTimersByTimeAsync(1000);

    expect(link().starts).toBe(2);
    expect(link().invoked).toEqual([
      ['JoinProject', 'p1'],
      ['JoinProject', 'p1'],
    ]);
    expect(realtime.connectionId()).toBe('conn-2');
    expect(reconnections).toHaveBeenCalledTimes(1);
  });

  it('tries again later when it cannot connect, and says why only in the log', async () => {
    vi.useFakeTimers();
    const joining = realtime.join('p1');
    links[0].failNextStart = true;
    await joining;

    expect(logger.warn).toHaveBeenCalled();
    expect(realtime.connectionId()).toBeNull();
    await vi.advanceTimersByTimeAsync(1000);

    expect(link().starts).toBe(2);
    expect(link().invoked).toEqual([['JoinProject', 'p1']]);
    expect(realtime.connectionId()).toBe('conn-2');
  });

  it('stops when the session ends, and does not start again by itself', async () => {
    vi.useFakeTimers();
    await realtime.join('p1');

    signedIn.set(false);
    TestBed.tick();
    await flush();
    link().close();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(link().stop).toHaveBeenCalled();
    expect(link().starts).toBe(1);
    expect(realtime.connectionId()).toBeNull();
  });

  it('connects anew for the next session, without the projects of the last one', async () => {
    await realtime.join('p1');
    signedIn.set(false);
    TestBed.tick();
    await flush();

    signedIn.set(true);
    await realtime.join('p2');

    expect(links).toHaveLength(2);
    expect(links[1].invoked).toEqual([['JoinProject', 'p2']]);
  });

  it('does not connect while signed out', async () => {
    signedIn.set(false);
    TestBed.tick();

    await realtime.join('p1');

    expect(links).toHaveLength(0);
    expect(realtime.connectionId()).toBeNull();
  });
});
