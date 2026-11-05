import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { isApiRequest } from '../api/api-client';
import { ANONYMOUS } from './auth-api';
import { AuthStore } from './auth-store';

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

/**
 * Sends the access token with API requests. A 401 means it expired: the request waits for a refresh,
 * shared with whatever else failed meanwhile, and goes once more with the new token. If the session is
 * over, the error goes on and the user goes to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiRequest(request.url) || request.context.get(ANONYMOUS)) {
    return next(request);
  }

  const auth = inject(AuthStore);
  const router = inject(Router);
  const usedToken = auth.accessToken();

  return next(withToken(request, usedToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return from(auth.refreshAfter(usedToken)).pipe(
        switchMap((fresh) => {
          if (!fresh) {
            void router.navigateByUrl(
              router
                .createUrlTree(['/login'], { queryParams: { returnUrl: router.url } })
                .toString(),
            );
            return throwError(() => error);
          }
          // Straight to the next handler, not through here again: a second 401 is final.
          return next(withToken(request, fresh));
        }),
      );
    }),
  );
};
