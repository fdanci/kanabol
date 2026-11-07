import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth-api';
import { CurrentUserDto } from './models';

/** The name every tab of the app takes its turn on, to refresh. */
export const REFRESH_LOCK = 'kb.auth.refresh';

/** Less than this left, and a token for the hub is refreshed first. */
const FRESH_FOR_SECONDS = 30;

interface TokenClaims {
  sub?: unknown;
  iat?: unknown;
  exp?: unknown;
}

/** What the token says, read without checking anything: the API checks it. Null when it can't be read. */
function claimsOf(token: string): TokenClaims | null {
  try {
    const payload = (token.split('.')[1] ?? '').replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(payload)) as TokenClaims;
  } catch {
    return null;
  }
}

/**
 * How long the token has: its lifetime, exp less iat, less the time since it came, since this computer's
 * clock may be minutes off the API's; from exp alone when it has no iat; 0 when it can't be read.
 */
function secondsLeftOf(token: string, receivedAt: number): number {
  const { iat, exp } = claimsOf(token) ?? {};
  if (typeof exp !== 'number') {
    return 0;
  }
  return typeof iat === 'number'
    ? exp - iat - (Date.now() - receivedAt) / 1000
    : exp - Date.now() / 1000;
}

/**
 * The session: the access token, kept in memory only, and the signed-in user (docs/SPEC.md §4.5).
 * The refresh token is an HttpOnly cookie the browser sends by itself, and this code never sees it.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly token = signal<string | null>(null);
  private readonly current = signal<CurrentUserDto | null>(null);
  private refreshing: Promise<string | null> | null = null;
  /** When the token came, by this computer's clock. */
  private receivedAt = 0;

  readonly accessToken = this.token.asReadonly();
  readonly user = this.current.asReadonly();
  readonly isSignedIn = computed(() => this.token() !== null && this.current() !== null);
  readonly mustChangePassword = computed(() => this.current()?.mustChangePassword ?? false);
  private readonly held = computed(() => new Set(this.current()?.permissions ?? []));

  has(permission: string): boolean {
    return this.held().has(permission);
  }

  /** Throws the HttpErrorResponse of a refused login, for the form to explain. */
  async login(email: string, password: string): Promise<void> {
    const session = await firstValueFrom(this.api.login(email, password));
    this.take(session.accessToken);
    await this.loadUser();
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const session = await firstValueFrom(this.api.changePassword(currentPassword, newPassword));
    this.take(session.accessToken);
    await this.loadUser();
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.logout());
    } catch {
      // The cookie can't be revoked now, but this tab forgets the session all the same.
    } finally {
      this.clear();
    }
  }

  /** At startup: the access token is gone with the page, so a session comes back only through the cookie. */
  async restore(): Promise<void> {
    try {
      if (await this.refresh()) {
        await this.loadUser();
      }
    } catch {
      // Unreachable or failing: the app starts signed out, and the login page says why when used.
      this.clear();
    }
  }

  /**
   * A new access token from the refresh cookie, or null when the session is over. Requests that ask
   * at the same time share one refresh. Tabs take turns through navigator.locks: two tabs sending
   * the same cookie at once would look like a stolen refresh token, and end the session (§4.5).
   */
  refresh(): Promise<string | null> {
    this.refreshing ??= this.inTurn(() => this.refreshNow()).finally(() => {
      this.refreshing = null;
    });
    return this.refreshing;
  }

  /**
   * For the hub (docs/SPEC.md §9): it carries the token in its address when it connects, and can't wait
   * for a 401, so a token with 30 seconds or less left, or one that can't be read, is refreshed first.
   * Null when signed out.
   */
  freshToken(): Promise<string | null> {
    const held = this.token();
    if (held === null) {
      return Promise.resolve(null);
    }
    return secondsLeftOf(held, this.receivedAt) > FRESH_FOR_SECONDS
      ? Promise.resolve(held)
      : this.refresh();
  }

  /** For a request refused with the token it carried: a newer one, if this tab has one already. */
  refreshAfter(usedToken: string | null): Promise<string | null> {
    const latest = this.token();
    return latest !== null && latest !== usedToken ? Promise.resolve(latest) : this.refresh();
  }

  clear(): void {
    this.token.set(null);
    this.current.set(null);
  }

  private async refreshNow(): Promise<string | null> {
    try {
      const session = await firstValueFrom(this.api.refresh());
      // The cookie is the browser's, shared by every tab: after someone else signed in elsewhere, it
      // answers for them. This tab ends its session rather than act as them under the wrong name.
      const signedIn = this.current();
      const subject = claimsOf(session.accessToken)?.sub;
      if (signedIn !== null && typeof subject === 'string' && subject !== signedIn.id) {
        this.clear();
        return null;
      }
      this.take(session.accessToken);
      return session.accessToken;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.clear();
        return null;
      }
      throw error;
    }
  }

  private take(token: string): void {
    this.token.set(token);
    this.receivedAt = Date.now();
  }

  private inTurn<T>(work: () => Promise<T>): Promise<T> {
    const locks = (navigator as Partial<Navigator>).locks;
    return locks ? locks.request(REFRESH_LOCK, work) : work();
  }

  private async loadUser(): Promise<void> {
    this.current.set(await firstValueFrom(this.api.me()));
  }
}
