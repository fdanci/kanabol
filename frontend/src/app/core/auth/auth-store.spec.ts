import { HttpErrorResponse } from '@angular/common/http';
import { Injector } from '@angular/core';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthStore, REFRESH_LOCK } from './auth-store';
import { AccessTokenDto, CurrentUserDto } from './models';

const ANA: CurrentUserDto = {
  id: '019a0000-0000-7000-8000-000000000001',
  email: 'ana@example.com',
  displayName: 'Ana Pop',
  roleName: 'Manager',
  mustChangePassword: false,
  permissions: ['users.manage', 'reports.view'],
};

function session(accessToken: string, mustChangePassword = false): AccessTokenDto {
  return { accessToken, expiresAt: '2026-11-06T10:15:00Z', mustChangePassword };
}

/** A token shaped like the API's, with the claims given; the signature is never checked here. */
function jwt(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/=+$/, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
  return `eyJhbGciOiJIUzI1NiJ9.${payload}.c2lnbmF0dXJl`;
}

function refused(status: number): Observable<never> {
  return throwError(() => new HttpErrorResponse({ status, url: '/api/auth/x' }));
}

class FakeAuthApi {
  login = vi.fn((): Observable<AccessTokenDto> => of(session('login-token')));
  refresh = vi.fn((): Observable<AccessTokenDto> => of(session('refreshed-token')));
  logout = vi.fn((): Observable<null> => of(null));
  changePassword = vi.fn((): Observable<AccessTokenDto> => of(session('changed-token')));
  me = vi.fn((): Observable<CurrentUserDto> => of(ANA));
}

/** A tab: its own store, as each tab has its own memory, over the browser's one set of locks. */
function tab(api: FakeAuthApi): AuthStore {
  return Injector.create({ providers: [AuthStore, { provide: AuthApi, useValue: api }] }).get(
    AuthStore,
  );
}

/** navigator.locks as browsers have it: one holder per name, the others wait their turn. */
function fakeLocks(): { requested: string[] } {
  const requested: string[] = [];
  const queues = new Map<string, Promise<unknown>>();
  const locks = {
    request: (name: string, callback: () => Promise<unknown>) => {
      requested.push(name);
      const before = queues.get(name) ?? Promise.resolve();
      const turn = before.then(callback, callback);
      queues.set(
        name,
        turn.catch(() => undefined),
      );
      return turn;
    },
  };
  Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });
  return { requested };
}

function everythingIn(storage: Storage): string {
  return Object.keys(storage)
    .map((key) => `${key}=${storage.getItem(key) ?? ''}`)
    .join(';');
}

// A store is a class with signals: plain dependency injection is enough, without TestBed.
describe('AuthStore', () => {
  let api: FakeAuthApi;

  beforeEach(() => {
    api = new FakeAuthApi();
    Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
  });

  it('keeps the access token in memory only', async () => {
    const store = tab(api);

    await store.login('ana@example.com', 'o parola buna');

    expect(store.accessToken()).toBe('login-token');
    expect(everythingIn(localStorage)).not.toContain('login-token');
    expect(everythingIn(sessionStorage)).not.toContain('login-token');
  });

  it('signs in with the user and their permissions', async () => {
    const store = tab(api);

    await store.login('ana@example.com', 'o parola buna');

    expect(api.login).toHaveBeenCalledWith('ana@example.com', 'o parola buna');
    expect(store.isSignedIn()).toBe(true);
    expect(store.user()?.email).toBe('ana@example.com');
    expect(store.has('users.manage')).toBe(true);
    expect(store.has('roles.manage')).toBe(false);
  });

  it('stays signed out when the login is refused, and passes the error on', async () => {
    api.login.mockReturnValue(refused(401));
    const store = tab(api);

    await expect(store.login('ana@example.com', 'gresita')).rejects.toBeInstanceOf(
      HttpErrorResponse,
    );

    expect(store.isSignedIn()).toBe(false);
    expect(store.accessToken()).toBeNull();
  });

  it('knows when the password has to change first', async () => {
    api.login.mockReturnValue(of(session('login-token', true)));
    api.me.mockReturnValue(of({ ...ANA, mustChangePassword: true }));
    const store = tab(api);

    await store.login('ana@example.com', 'parola initiala');

    expect(store.mustChangePassword()).toBe(true);
  });

  it('takes the new session after a password change', async () => {
    api.login.mockReturnValue(of(session('login-token', true)));
    api.me.mockReturnValueOnce(of({ ...ANA, mustChangePassword: true }));
    const store = tab(api);
    await store.login('ana@example.com', 'parola initiala');

    await store.changePassword('parola initiala', 'parola noua aleasa');

    expect(api.changePassword).toHaveBeenCalledWith('parola initiala', 'parola noua aleasa');
    expect(store.accessToken()).toBe('changed-token');
    expect(store.mustChangePassword()).toBe(false);
  });

  it('refreshes once for everyone who asks at the same time', async () => {
    const answer = new Subject<AccessTokenDto>();
    api.refresh.mockReturnValue(answer);
    const store = tab(api);

    const first = store.refresh();
    const second = store.refresh();
    answer.next(session('refreshed-token'));
    answer.complete();

    expect(await first).toBe('refreshed-token');
    expect(await second).toBe('refreshed-token');
    expect(api.refresh).toHaveBeenCalledTimes(1);
  });

  it('refreshes again once the previous refresh is over', async () => {
    const store = tab(api);

    await store.refresh();
    await store.refresh();

    expect(api.refresh).toHaveBeenCalledTimes(2);
  });

  it('uses a token refreshed meanwhile instead of refreshing again', async () => {
    const store = tab(api);
    await store.login('ana@example.com', 'o parola buna');
    await store.refresh();

    const token = await store.refreshAfter('login-token');

    expect(token).toBe('refreshed-token');
    expect(api.refresh).toHaveBeenCalledTimes(1);
  });

  it('ends the session when the refresh is refused', async () => {
    api.refresh.mockReturnValue(refused(401));
    const store = tab(api);
    await store.login('ana@example.com', 'o parola buna');

    expect(await store.refresh()).toBeNull();

    expect(store.isSignedIn()).toBe(false);
    expect(store.accessToken()).toBeNull();
  });

  it('keeps the session when the refresh fails for another reason, and passes the error on', async () => {
    api.refresh.mockReturnValue(refused(0));
    const store = tab(api);
    await store.login('ana@example.com', 'o parola buna');

    await expect(store.refresh()).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(store.accessToken()).toBe('login-token');
  });

  // Two tabs that send the same refresh token look like a stolen one (docs/SPEC.md §4.5).
  it('refreshes in one tab at a time, so the next one sends the rotated cookie', async () => {
    const { requested } = fakeLocks();
    const firstAnswer = new Subject<AccessTokenDto>();
    const firstApi = new FakeAuthApi();
    const secondApi = new FakeAuthApi();
    firstApi.refresh.mockReturnValue(firstAnswer);
    const first = tab(firstApi);
    const second = tab(secondApi);

    const firstRefresh = first.refresh();
    const secondRefresh = second.refresh();
    await Promise.resolve();

    expect(firstApi.refresh).toHaveBeenCalledTimes(1);
    expect(secondApi.refresh).not.toHaveBeenCalled();

    firstAnswer.next(session('first-tab-token'));
    firstAnswer.complete();

    expect(await firstRefresh).toBe('first-tab-token');
    expect(await secondRefresh).toBe('refreshed-token');
    expect(secondApi.refresh).toHaveBeenCalledTimes(1);
    expect(requested).toEqual(['kb.auth.refresh', 'kb.auth.refresh']);
  });

  it('restores the session from the cookie when the app starts', async () => {
    const store = tab(api);

    await store.restore();

    expect(store.accessToken()).toBe('refreshed-token');
    expect(store.user()?.email).toBe('ana@example.com');
  });

  it('starts signed out when there is no session to restore', async () => {
    api.refresh.mockReturnValue(refused(401));
    const store = tab(api);

    await store.restore();

    expect(store.isSignedIn()).toBe(false);
  });

  it('starts signed out when the server does not answer', async () => {
    api.refresh.mockReturnValue(refused(0));
    const store = tab(api);

    await store.restore();

    expect(store.isSignedIn()).toBe(false);
  });

  it('forgets the session on logout, even if the server does not answer', async () => {
    api.logout.mockReturnValue(refused(0));
    const store = tab(api);
    await store.login('ana@example.com', 'o parola buna');

    await store.logout();

    expect(api.logout).toHaveBeenCalled();
    expect(store.isSignedIn()).toBe(false);
    expect(store.accessToken()).toBeNull();
    expect(store.user()).toBeNull();
  });

  describe('at the edges', () => {
    it('lets the next refresh try again after one failed on the network', async () => {
      api.refresh.mockReturnValueOnce(refused(0));
      const store = tab(api);

      await expect(store.refresh()).rejects.toBeInstanceOf(HttpErrorResponse);
      expect(await store.refresh()).toBe('refreshed-token');

      expect(api.refresh).toHaveBeenCalledTimes(2);
    });

    it('answers everyone waiting on a refused refresh with no token', async () => {
      const answer = new Subject<AccessTokenDto>();
      api.refresh.mockReturnValue(answer);
      const store = tab(api);
      await store.login('ana@example.com', 'o parola buna');

      const waiting = [
        store.refreshAfter('login-token'),
        store.refreshAfter('login-token'),
        store.refresh(),
      ];
      answer.error(new HttpErrorResponse({ status: 401, url: '/api/auth/refresh' }));

      expect(await Promise.all(waiting)).toEqual([null, null, null]);
      expect(api.refresh).toHaveBeenCalledTimes(1);
    });

    it('refreshes when it has no token at all', async () => {
      const store = tab(api);

      expect(await store.refreshAfter(null)).toBe('refreshed-token');
      expect(api.refresh).toHaveBeenCalledTimes(1);
    });

    // The lock is per name, so tabs of other apps on the same origin don't wait on it.
    it('takes its turn under its own name, and lets the next tab go even when it fails', async () => {
      const { requested } = fakeLocks();
      const failing = new FakeAuthApi();
      failing.refresh.mockReturnValue(refused(0));
      const first = tab(failing);
      const second = tab(api);

      const results = await Promise.allSettled([first.refresh(), second.refresh()]);

      expect(results.map((result) => result.status)).toEqual(['rejected', 'fulfilled']);
      expect(requested).toEqual([REFRESH_LOCK, REFRESH_LOCK]);
      expect(REFRESH_LOCK).toBe('kb.auth.refresh');
    });

    it('restores a session whose password has to change, and goes no further', async () => {
      api.refresh.mockReturnValue(of(session('refreshed-token', true)));
      api.me.mockReturnValue(of({ ...ANA, mustChangePassword: true }));
      const store = tab(api);

      await store.restore();

      expect(store.isSignedIn()).toBe(true);
      expect(store.mustChangePassword()).toBe(true);
    });
  });

  // docs/SPEC.md §4.5 and §9: a hub connection carries the token in its address, and can't wait for a 401.
  describe('a fresh token for the hub', () => {
    /** A token shaped like the API's, which expires so many seconds from now, without iat. */
    function expiringIn(seconds: number): string {
      return jwt({ sub: ANA.id, exp: Math.floor(Date.now() / 1000) + seconds });
    }

    it('is the token it has while that has more than 30 seconds left', async () => {
      const held = expiringIn(600);
      api.login.mockReturnValue(of(session(held)));
      const store = tab(api);
      await store.login('ana@example.com', 'parola');

      expect(await store.freshToken()).toBe(held);
      expect(api.refresh).not.toHaveBeenCalled();
    });

    it('is a refreshed one when the token is about to expire', async () => {
      api.login.mockReturnValue(of(session(expiringIn(20))));
      const store = tab(api);
      await store.login('ana@example.com', 'parola');

      expect(await store.freshToken()).toBe('refreshed-token');
      expect(api.refresh).toHaveBeenCalledTimes(1);
    });

    it('is a refreshed one when the token cannot be read', async () => {
      api.login.mockReturnValue(of(session('not-a-jwt')));
      const store = tab(api);
      await store.login('ana@example.com', 'parola');

      expect(await store.freshToken()).toBe('refreshed-token');
    });

    it('is none when signed out, without asking for one', async () => {
      const store = tab(api);

      expect(await store.freshToken()).toBeNull();
      expect(api.refresh).not.toHaveBeenCalled();
    });

    // The API's clock decides when a token expires; this computer's may be minutes off. The token's own
    // lifetime, exp less iat, counted from when it came, doesn't depend on either.
    it('counts the token from when it came, whatever the clock of this computer says', async () => {
      vi.useFakeTimers();
      try {
        const issuedAnHourAgo = Math.floor(Date.now() / 1000) - 3600;
        const held = jwt({ sub: ANA.id, iat: issuedAnHourAgo, exp: issuedAnHourAgo + 900 });
        api.login.mockReturnValue(of(session(held)));
        const store = tab(api);
        await store.login('ana@example.com', 'parola');

        expect(await store.freshToken()).toBe(held);
        vi.advanceTimersByTime(875_000);

        expect(await store.freshToken()).toBe('refreshed-token');
      } finally {
        vi.useRealTimers();
      }
    });
  });

  // The refresh cookie is the browser's, shared by every tab: after someone else signs in elsewhere, a
  // refresh here answers for them. This tab ends its session rather than act as them under the wrong name.
  describe('a refresh that answers for someone else', () => {
    it('ends the session of this tab', async () => {
      api.login.mockReturnValue(of(session(jwt({ sub: ANA.id }))));
      api.refresh.mockReturnValue(
        of(session(jwt({ sub: '019a0000-0000-7000-8000-0000000000b2' }))),
      );
      const store = tab(api);
      await store.login('ana@example.com', 'parola');

      expect(await store.refresh()).toBeNull();
      expect(store.isSignedIn()).toBe(false);
      expect(store.accessToken()).toBeNull();
    });

    it('keeps the session when it answers for the same user', async () => {
      const again = jwt({ sub: ANA.id, n: 2 });
      api.login.mockReturnValue(of(session(jwt({ sub: ANA.id }))));
      api.refresh.mockReturnValue(of(session(again)));
      const store = tab(api);
      await store.login('ana@example.com', 'parola');

      expect(await store.refresh()).toBe(again);
      expect(store.isSignedIn()).toBe(true);
    });
  });
});
