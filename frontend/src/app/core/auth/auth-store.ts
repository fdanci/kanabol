import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth-api';
import { CurrentUserDto } from './models';

/** The name every tab of the app takes its turn on, to refresh. */
export const REFRESH_LOCK = 'kb.auth.refresh';

/** Less than this left, and a token for the hub is refreshed first. */
const FRESH_FOR_SECONDS = 30;

/** How long the token has, from its exp claim; 0 when it can't be read. Reading it checks nothing. */
function secondsLeftOf(token: string): number {
  try {
    const payload = (token.split('.')[1] ?? '').replace(/-/g, '+').replace(/_/g, '/');
    const { exp } = JSON.parse(atob(payload)) as { exp?: unknown };
    return typeof exp === 'number' ? exp - Date.now() / 1000 : 0;
  } catch {
    return 0;
  }
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
    this.token.set(session.accessToken);
    await this.loadUser();
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    const session = await firstValueFrom(this.api.changePassword(currentPassword, newPassword));
    this.token.set(session.accessToken);
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
    return secondsLeftOf(held) > FRESH_FOR_SECONDS ? Promise.resolve(held) : this.refresh();
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
      this.token.set(session.accessToken);
      return session.accessToken;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.clear();
        return null;
      }
      throw error;
    }
  }

  private inTurn<T>(work: () => Promise<T>): Promise<T> {
    const locks = (navigator as Partial<Navigator>).locks;
    return locks ? locks.request(REFRESH_LOCK, work) : work();
  }

  private async loadUser(): Promise<void> {
    this.current.set(await firstValueFrom(this.api.me()));
  }
}
