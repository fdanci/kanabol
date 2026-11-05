import { HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { QUIET } from '../api/error-interceptor';
import { AccessTokenDto, CurrentUserDto } from './models';

/**
 * Set on the requests the refresh cookie proves, which carry no access token and whose 401 means
 * what it says: a wrong password, or a session that's over. Refreshing after one of those would loop.
 */
export const ANONYMOUS = new HttpContextToken<boolean>(() => false);

function anonymous(): HttpContext {
  return new HttpContext().set(ANONYMOUS, true).set(QUIET, true);
}

/** POST /api/auth/* and GET /api/auth/me (docs/SPEC.md §4.5). */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly api = inject(ApiClient);

  login(email: string, password: string): Observable<AccessTokenDto> {
    return this.api.post<AccessTokenDto>(
      '/auth/login',
      { email, password },
      { context: anonymous() },
    );
  }

  refresh(): Observable<AccessTokenDto> {
    return this.api.post<AccessTokenDto>('/auth/refresh', null, { context: anonymous() });
  }

  logout(): Observable<null> {
    return this.api.post<null>('/auth/logout', null, { context: anonymous() });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<AccessTokenDto> {
    return this.api.post<AccessTokenDto>('/auth/change-password', { currentPassword, newPassword });
  }

  me(): Observable<CurrentUserDto> {
    return this.api.get<CurrentUserDto>('/auth/me');
  }
}
