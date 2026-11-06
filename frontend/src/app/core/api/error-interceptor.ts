import {
  HttpContext,
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails, problemIn } from '../../shared/models/problem-details';
import { Snackbar } from '../../shared/ui/snackbar';
import { isApiRequest } from './api-client';
import { CORRELATION_HEADER } from './correlation-interceptor';

/** Set on a request whose caller shows its own errors, such as the login form. */
export const QUIET = new HttpContextToken<boolean>(() => false);

/** The codes a caller shows by itself, such as an email already taken, under its field. */
export const HANDLED_CODES = new HttpContextToken<readonly string[]>(() => []);

/** The context of a request whose caller shows these codes itself: the snackbar shows the rest. */
export function handledHere(...codes: string[]): HttpContext {
  return new HttpContext().set(HANDLED_CODES, codes);
}

const BY_CODE: Record<string, () => string> = {
  'Auth.PermissionRequired': () => $localize`Nu ai permisiunea pentru această acțiune.`,
  'Auth.PasswordChangeRequired': () => $localize`Schimbă-ți întâi parola.`,
  'Auth.TooManyLoginAttempts': () =>
    $localize`Prea multe încercări. Așteaptă un minut și reîncearcă.`,
  'User.ChangedMeanwhile': () =>
    $localize`Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.`,
  'Role.ChangedMeanwhile': () =>
    $localize`Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.`,
  // Who can give what (docs/SPEC.md §4.4).
  'Role.PermissionsBeyondYours': () =>
    $localize`Nu poți da o permisiune pe care nu o ai și nici schimba un rol care are una.`,
  'Role.SystemRoleIsFixed': () => $localize`Rolul de sistem nu se poate modifica sau șterge.`,
  'Role.InUse': () =>
    $localize`Rolul e dat încă unor utilizatori, inclusiv dezactivați. Mută-i întâi în alt rol.`,
  'User.RoleBeyondYours': () => $localize`Nu poți da un rol cu permisiuni pe care nu le ai.`,
  'User.PermissionsBeyondYours': () =>
    $localize`Nu poți schimba pe cineva care are permisiuni pe care nu le ai.`,
  'User.LastAdmin': () =>
    $localize`Este ultimul administrator activ. Dă întâi rolul Admin altcuiva.`,
  'User.OwnPasswordReset': () =>
    $localize`Parola ta o schimbi cu parola curentă, nu prin resetare.`,
  // The role on a project (docs/SPEC.md §4.4) and what keeps a project whole.
  'Project.MemberRequired': () => $localize`Nu ai acces la acest proiect.`,
  'Project.OwnerRequired': () => $localize`Doar un owner al proiectului poate face asta.`,
  'Project.NotFound': () => $localize`Proiectul nu mai există.`,
  'Project.KeyTaken': () => $localize`Există deja un proiect cu această cheie.`,
  'Project.Archived': () => $localize`Proiectul e arhivat. Restaurează-l întâi.`,
  'Project.AlreadyAMember': () => $localize`Utilizatorul e deja în proiect.`,
  'Project.MemberNotFound': () => $localize`Utilizatorul nu mai e în proiect. Reîncarcă pagina.`,
  'Project.LastOwner': () =>
    $localize`Proiectul are nevoie de un owner. Fă întâi pe altcineva owner.`,
  'Project.ChangedMeanwhile': () =>
    $localize`Proiectul s-a schimbat între timp. Reîncarcă pagina și reîncearcă.`,
  // What keeps a task whole (docs/SPEC.md §5.1, §5.3).
  'Task.NotFound': () => $localize`Task-ul nu mai există sau a fost arhivat.`,
  'Task.ChangedMeanwhile': () =>
    $localize`Task-ul s-a schimbat între timp. L-am citit din nou, reîncearcă.`,
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
 * to the session, which refreshes it, errors on fields belong to their form, and so do the codes a
 * request says it handles (handledHere).
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
        error.status === 401 ||
        (error.status === 400 && problem?.errors !== undefined) ||
        (problem?.code !== undefined && request.context.get(HANDLED_CODES).includes(problem.code));
      if (!handledElsewhere) {
        const correlationId = problem?.correlationId ?? error.headers.get(CORRELATION_HEADER);
        snackbar.error(messageFor(error.status, problem), correlationId);
      }

      return throwError(() => error);
    }),
  );
};
