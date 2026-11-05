import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails, problemIn } from '../../shared/models/problem-details';
import { Snackbar } from '../../shared/ui/snackbar';
import { isApiRequest } from './api-client';
import { CORRELATION_HEADER } from './correlation-interceptor';

/** Set on a request whose caller shows its own errors, such as the login form. */
export const QUIET = new HttpContextToken<boolean>(() => false);

const BY_CODE: Record<string, () => string> = {
  'Auth.PermissionRequired': () => $localize`Nu ai permisiunea pentru această acțiune.`,
  'Auth.PasswordChangeRequired': () => $localize`Schimbă-ți întâi parola.`,
  'Auth.TooManyLoginAttempts': () =>
    $localize`Prea multe încercări. Așteaptă un minut și reîncearcă.`,
  'User.ChangedMeanwhile': () =>
    $localize`Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.`,
  'Role.ChangedMeanwhile': () =>
    $localize`Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.`,
};

function messageFor(status: number, problem: ProblemDetails | null): string {
  const byCode = problem?.code ? BY_CODE[problem.code] : undefined;
  if (byCode) {
    return byCode();
  }

  if (status === 0) {
    return $localize`Serverul nu răspunde. Verifică conexiunea și reîncearcă.`;
  }
  if (status === 403) {
    return $localize`Nu ai permisiunea pentru această acțiune.`;
  }
  if (status === 404) {
    return $localize`Nu am găsit ce ai cerut. Poate a fost șters între timp.`;
  }
  if (status === 409) {
    return $localize`Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.`;
  }
  if (status >= 500) {
    return $localize`A apărut o eroare pe server. Reîncearcă, iar dacă se repetă, trimite codul administratorului.`;
  }
  return $localize`Cererea nu a putut fi îndeplinită. Reîncearcă, iar dacă se repetă, trimite codul administratorului.`;
}

/**
 * Turns a failed API request into a snackbar with its code, then passes the error on. A 401 belongs
 * to the session, which refreshes it, and errors on fields belong to their form.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const snackbar = inject(Snackbar);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        !(error instanceof HttpErrorResponse) ||
        !isApiRequest(request.url) ||
        request.context.get(QUIET)
      ) {
        return throwError(() => error);
      }

      const problem = problemIn(error);
      const handledElsewhere =
        error.status === 401 || (error.status === 400 && problem?.errors !== undefined);
      if (!handledElsewhere) {
        const correlationId = problem?.correlationId ?? error.headers.get(CORRELATION_HEADER);
        snackbar.error(messageFor(error.status, problem), correlationId);
      }

      return throwError(() => error);
    }),
  );
};
