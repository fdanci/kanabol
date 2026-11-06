import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { QUIET } from '../api/error-interceptor';
import { AuthStore } from '../auth/auth-store';
import { CLIENT_LOG_LIMIT, ClientLogEntry, ClientLogs } from './client-logs';

function entry(message: string, level: 'warn' | 'error' = 'error'): ClientLogEntry {
  return { level, message, url: 'https://kanabol.local/users' };
}

describe('ClientLogs', () => {
  let http: HttpTestingController;
  let logs: ClientLogs;
  let signedIn: ReturnType<typeof signal<boolean>>;
  let mustChange: ReturnType<typeof signal<boolean>>;

  beforeEach(() => {
    vi.useFakeTimers();
    signedIn = signal(true);
    mustChange = signal(false);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { isSignedIn: signedIn, mustChangePassword: mustChange } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    logs = TestBed.inject(ClientLogs);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function sent() {
    return http.match('/api/client-logs');
  }

  it('gathers what is logged within 5 seconds into one quiet batch', () => {
    logs.add(entry('one', 'warn'));
    vi.advanceTimersByTime(3_000);
    logs.add(entry('two'));
    expect(sent()).toHaveLength(0);

    vi.advanceTimersByTime(2_000);

    const [request] = sent();
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      entries: [entry('one', 'warn'), entry('two')],
      dropped: 0,
    });
    // A failure to log must not show a snackbar, or log itself.
    expect(request.request.context.get(QUIET)).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('sends at most CLIENT_LOG_LIMIT, 10, a minute, and then says how many it dropped', () => {
    expect(CLIENT_LOG_LIMIT).toBe(10);
    for (let i = 0; i < 13; i++) {
      logs.add(entry(`e${String(i)}`));
    }
    vi.advanceTimersByTime(5_000);

    const [first] = sent();
    expect((first.request.body as { entries: unknown[] }).entries).toHaveLength(10);
    expect((first.request.body as { dropped: number }).dropped).toBe(3);
    first.flush(null);

    vi.advanceTimersByTime(30_000);
    logs.add(entry('still the same minute'));
    vi.advanceTimersByTime(5_000);
    expect(sent()).toHaveLength(0);

    vi.advanceTimersByTime(25_000);
    logs.add(entry('a new minute'));
    vi.advanceTimersByTime(5_000);
    const [second] = sent();
    expect(second.request.body).toEqual({ entries: [entry('a new minute')], dropped: 1 });
    second.flush(null);
  });

  it('sends nothing for someone signed out, whose request would only end on the login page', () => {
    signedIn.set(false);
    logs.add(entry('before login'));
    vi.advanceTimersByTime(5_000);
    expect(sent()).toHaveLength(0);

    signedIn.set(true);
    logs.add(entry('after login'));
    vi.advanceTimersByTime(5_000);

    const [request] = sent();
    expect(request.request.body).toEqual({ entries: [entry('after login')], dropped: 0 });
    request.flush(null);
  });

  it('sends nothing while the password must change, which the API would refuse', () => {
    mustChange.set(true);
    logs.add(entry('on the password page'));
    vi.advanceTimersByTime(5_000);

    expect(sent()).toHaveLength(0);
  });

  it('lets a failed batch go, without throwing', () => {
    logs.add(entry('lost'));
    vi.advanceTimersByTime(5_000);

    expect(() => {
      sent()[0].flush({ status: 500 }, { status: 500, statusText: 'Error' });
    }).not.toThrow();
  });

  it('cuts what the API would refuse as too long', () => {
    logs.add({
      level: 'error',
      message: 'm'.repeat(5_000),
      url: 'u'.repeat(3_000),
      details: 'd'.repeat(20_000),
      correlationId: 'c'.repeat(200),
    });
    vi.advanceTimersByTime(5_000);

    const [request] = sent();
    const [cut] = (request.request.body as { entries: ClientLogEntry[] }).entries;
    expect(cut.message).toHaveLength(4_000);
    expect(cut.url).toHaveLength(2_000);
    expect(cut.details).toHaveLength(16_000);
    expect(cut.correlationId).toHaveLength(100);
    request.flush(null);
  });
});
