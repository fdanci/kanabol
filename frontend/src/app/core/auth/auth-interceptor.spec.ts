import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth-interceptor';
import { AuthStore } from './auth-store';

const ME = {
  id: '019a0000-0000-7000-8000-000000000001',
  email: 'ana@example.com',
  displayName: 'Ana Pop',
  roleName: 'Manager',
  mustChangePassword: false,
  permissions: ['users.manage'],
};

function token(value: string) {
  return { accessToken: value, expiresAt: '2026-11-06T10:15:00Z', mustChangePassword: false };
}

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let store: AuthStore;

  beforeEach(() => {
    Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
  });

  afterEach(() => {
    http.verify();
  });

  async function signedIn(accessToken = 'first-token'): Promise<void> {
    const signing = store.login('ana@example.com', 'o parola buna');
    http.expectOne('/api/auth/login').flush(token(accessToken));
    await Promise.resolve();
    http.expectOne('/api/auth/me').flush(ME);
    await signing;
  }

  function bearerOf(request: TestRequest): string | null {
    return request.request.headers.get('Authorization');
  }

  /** Lets the refresh, which runs through promises, reach the backend. */
  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
  }

  it('sends the access token with API requests', async () => {
    await signedIn();

    client.get('/api/users').subscribe();

    expect(bearerOf(http.expectOne('/api/users'))).toBe('Bearer first-token');
  });

  it('sends no token outside the API', async () => {
    await signedIn();

    client.get('/brand/kanabol-mark.svg').subscribe();

    expect(bearerOf(http.expectOne('/brand/kanabol-mark.svg'))).toBeNull();
  });

  it('sends no token to login, refresh and logout, which only need the cookie', async () => {
    await signedIn();

    void store.refresh();
    await settle();

    expect(bearerOf(http.expectOne('/api/auth/refresh'))).toBeNull();
    http.match('/api/auth/refresh').forEach((request) => {
      request.flush(token('x'));
    });
  });

  it('refreshes after a 401 and sends the request again with the new token', async () => {
    await signedIn();
    let answer: unknown;
    client.get('/api/users').subscribe((body) => (answer = body));

    http
      .expectOne('/api/users')
      .flush({ code: 'Auth.SessionEnded' }, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush(token('second-token'));
    await settle();
    const retried = http.expectOne('/api/users');
    retried.flush({ items: [] });

    expect(bearerOf(retried)).toBe('Bearer second-token');
    expect(answer).toEqual({ items: [] });
  });

  it('refreshes once for requests that fail together, then sends them all again', async () => {
    await signedIn();
    client.get('/api/users').subscribe();
    client.get('/api/roles').subscribe();

    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/roles').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush(token('second-token'));
    await settle();

    expect(bearerOf(http.expectOne('/api/users'))).toBe('Bearer second-token');
    expect(bearerOf(http.expectOne('/api/roles'))).toBe('Bearer second-token');
  });

  it('does not refresh again for a request that failed with a token refreshed since', async () => {
    await signedIn();
    client.get('/api/users').subscribe();
    client.get('/api/roles').subscribe();
    const late = http.expectOne('/api/roles');

    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush(token('second-token'));
    await settle();
    http.expectOne('/api/users').flush({});
    late.flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();

    http.expectNone('/api/auth/refresh');
    expect(bearerOf(http.expectOne('/api/roles'))).toBe('Bearer second-token');
  });

  it('tries only once: a second 401 is passed on', async () => {
    await signedIn();
    let caught: unknown;
    client.get('/api/users').subscribe({ error: (error: unknown) => (caught = error) });

    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http.expectOne('/api/auth/refresh').flush(token('second-token'));
    await settle();
    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();

    http.expectNone('/api/auth/refresh');
    expect(caught).toBeInstanceOf(HttpErrorResponse);
  });

  it('ends the session and goes to the login page when the refresh is refused', async () => {
    await signedIn();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    let caught: unknown;
    client.get('/api/users').subscribe({ error: (error: unknown) => (caught = error) });

    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    http
      .expectOne('/api/auth/refresh')
      .flush({ code: 'Auth.InvalidRefreshToken' }, { status: 401, statusText: 'Unauthorized' });
    await settle();

    expect((caught as HttpErrorResponse).status).toBe(401);
    expect(store.isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(expect.stringMatching(/^\/login/));
  });

  it('leaves a refused login to the login form', async () => {
    let caught: unknown;
    store.login('ana@example.com', 'gresita').catch((error: unknown) => (caught = error));

    http
      .expectOne('/api/auth/login')
      .flush({ code: 'Auth.InvalidCredentials' }, { status: 401, statusText: 'Unauthorized' });
    await settle();

    http.expectNone('/api/auth/refresh');
    expect(caught).toBeInstanceOf(HttpErrorResponse);
  });
});
