import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Snackbar } from '../../shared/ui/snackbar';
import { QUIET, errorInterceptor, handledHere } from './error-interceptor';

describe('errorInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: Snackbar, useValue: snackbar },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  function failed(
    status: number,
    body: string | object,
    headers: Record<string, string> = {},
    context?: HttpContext,
  ): unknown {
    let caught: unknown;
    client.get('/api/x', { context }).subscribe({ error: (error: unknown) => (caught = error) });
    http.expectOne('/api/x').flush(body, { status, statusText: 'Refused', headers });
    return caught;
  }

  it('shows a server error with its code, and passes the error on', () => {
    const caught = failed(500, {
      status: 500,
      code: 'Unexpected',
      correlationId: 'b7e1c2d4-0000-4000-8000-00000a1b2c3d',
    });

    expect(snackbar.error).toHaveBeenCalledWith(
      'A apărut o eroare pe server. Reîncearcă, iar dacă se repetă, trimite codul administratorului.',
      'b7e1c2d4-0000-4000-8000-00000a1b2c3d',
    );
    expect(caught).toBeInstanceOf(HttpErrorResponse);
  });

  it('takes the id from the header when the body has none', () => {
    failed(502, 'Bad gateway', { 'X-Correlation-Id': 'from-the-header' });

    expect(snackbar.error).toHaveBeenCalledWith(expect.any(String), 'from-the-header');
  });

  it('says what a missing permission means', () => {
    failed(403, { status: 403, code: 'Auth.PermissionRequired', correlationId: 'id' });

    expect(snackbar.error).toHaveBeenCalledWith('Nu ai permisiunea pentru această acțiune.', 'id');
  });

  it('says what a change made meanwhile means', () => {
    failed(409, { status: 409, code: 'User.ChangedMeanwhile', correlationId: 'id' });

    expect(snackbar.error).toHaveBeenCalledWith(
      'Datele s-au schimbat între timp. Reîncarcă pagina și reîncearcă.',
      'id',
    );
  });

  it('says the server is unreachable when nothing answers', () => {
    let caught: unknown;
    client.get('/api/x').subscribe({ error: (error: unknown) => (caught = error) });
    http.expectOne('/api/x').error(new ProgressEvent('error'));

    expect(snackbar.error).toHaveBeenCalledWith(
      'Serverul nu răspunde. Verifică conexiunea și reîncearcă.',
      null,
    );
    expect(caught).toBeInstanceOf(HttpErrorResponse);
  });

  it('leaves a 401 to the session, which refreshes it', () => {
    failed(401, { status: 401, code: 'Auth.SessionEnded' });

    expect(snackbar.error).not.toHaveBeenCalled();
  });

  it('leaves the errors of a field to its form', () => {
    const caught = failed(400, {
      status: 400,
      code: 'Validation',
      errors: { email: ['This isn’t an email address.'] },
    });

    expect(snackbar.error).not.toHaveBeenCalled();
    expect(caught).toBeInstanceOf(HttpErrorResponse);
  });

  it('stays quiet for a request that handles its own errors', () => {
    failed(500, { status: 500 }, {}, new HttpContext().set(QUIET, true));

    expect(snackbar.error).not.toHaveBeenCalled();
  });

  it('leaves to the caller the codes it says it handles, and shows the rest', () => {
    const context = handledHere('User.EmailTaken');

    const caught = failed(409, { status: 409, code: 'User.EmailTaken' }, {}, context);
    expect(snackbar.error).not.toHaveBeenCalled();
    expect(caught).toBeInstanceOf(HttpErrorResponse);

    failed(409, { status: 409, code: 'User.LastAdmin', correlationId: 'id' }, {}, context);
    expect(snackbar.error).toHaveBeenCalledWith(
      'Este ultimul administrator activ. Dă întâi rolul Admin altcuiva.',
      'id',
    );
  });

  it.each([
    [
      'Role.PermissionsBeyondYours',
      'Nu poți da o permisiune pe care nu o ai și nici schimba un rol care are una.',
    ],
    ['Role.SystemRoleIsFixed', 'Rolul de sistem nu se poate modifica sau șterge.'],
    [
      'Role.InUse',
      'Rolul e dat încă unor utilizatori, inclusiv dezactivați. Mută-i întâi în alt rol.',
    ],
    ['User.RoleBeyondYours', 'Nu poți da un rol cu permisiuni pe care nu le ai.'],
    [
      'User.PermissionsBeyondYours',
      'Nu poți schimba pe cineva care are permisiuni pe care nu le ai.',
    ],
    ['User.LastAdmin', 'Este ultimul administrator activ. Dă întâi rolul Admin altcuiva.'],
    ['User.OwnPasswordReset', 'Parola ta o schimbi cu parola curentă, nu prin resetare.'],
  ])('says what %s means', (code, message) => {
    failed(
      code === 'User.LastAdmin' || code.endsWith('IsFixed') || code.endsWith('InUse') ? 409 : 403,
      {
        code,
        correlationId: 'id',
      },
    );

    expect(snackbar.error).toHaveBeenCalledWith(message, 'id');
  });
});
