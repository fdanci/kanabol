import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus, ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DEFAULT_OPTIONS } from '@angular/material/dialog';
import { MatIconRegistry } from '@angular/material/icon';
import { MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS } from '@angular/material/slide-toggle';
import { MAT_TOOLTIP_DEFAULT_OPTIONS } from '@angular/material/tooltip';
import { appConfig } from './app.config';
import { AuthStore } from './core/auth/auth-store';
import { GlobalErrorHandler } from './core/logging/global-error-handler';
import { Snackbar } from './shared/ui/snackbar';

describe('appConfig', () => {
  const root = document.documentElement;
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    delete root.dataset['theme'];
    // jsdom has neither.
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: vi.fn() }));
    Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: Snackbar, useValue: snackbar },
      ],
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Starts the app, answering the refresh it sends to restore the session. */
  async function started(refresh: (http: HttpTestingController) => void): Promise<void> {
    const done = TestBed.inject(ApplicationInitStatus).donePromise;
    const http = TestBed.inject(HttpTestingController);
    refresh(http);
    await done;
  }

  function noSession(http: HttpTestingController): void {
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
  }

  it('applies the saved theme before the app starts', async () => {
    localStorage.setItem('kb.theme', 'dark');

    await started(noSession);

    expect(root.dataset['theme']).toBe('dark');
  });

  it('restores the session from its cookie before the first page', async () => {
    const done = TestBed.inject(ApplicationInitStatus).donePromise;
    const http = TestBed.inject(HttpTestingController);

    http
      .expectOne('/api/auth/refresh')
      .flush({ accessToken: 't', expiresAt: '2026-11-06T10:15:00Z', mustChangePassword: false });
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
    http.expectOne('/api/auth/me').flush({
      id: '1',
      email: 'ana@example.com',
      displayName: 'Ana',
      roleName: 'Manager',
      mustChangePassword: false,
      permissions: [],
    });
    await done;

    expect(TestBed.inject(AuthStore).isSignedIn()).toBe(true);
  });

  it('starts signed out, without an error, when there is no session', async () => {
    await started(noSession);

    expect(TestBed.inject(AuthStore).isSignedIn()).toBe(false);
    expect(snackbar.error).not.toHaveBeenCalled();
  });

  it('sends API requests with an id and a token, and shows what fails', async () => {
    await started(noSession);
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(HttpClient)
      .get('/api/users')
      .subscribe({ error: () => undefined });
    const request = http.expectOne('/api/users');
    request.flush({ status: 500 }, { status: 500, statusText: 'Error' });

    expect(request.request.headers.has('X-Correlation-Id')).toBe(true);
    expect(snackbar.error).toHaveBeenCalled();
  });

  it('hands uncaught errors to GlobalErrorHandler', () => {
    expect(TestBed.inject(ErrorHandler)).toBeInstanceOf(GlobalErrorHandler);
  });

  // docs/UI_UX_SPEC.md §6.0.3, §6.0.5 and §6.0.6.
  it('sets the controls up as the UI spec has them', async () => {
    await started(noSession);

    expect(TestBed.inject(MatIconRegistry).getDefaultFontSetClass()).toEqual(['kb-symbols']);
    expect(TestBed.inject(MAT_TOOLTIP_DEFAULT_OPTIONS).showDelay).toBe(500);
    expect(TestBed.inject(MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS).hideIcon).toBe(true);
    expect(TestBed.inject(MAT_DIALOG_DEFAULT_OPTIONS)).toMatchObject({
      panelClass: 'kb-sheet',
      backdropClass: 'kb-sheet-backdrop',
      position: { top: '12vh' },
      enterAnimationDuration: 240,
      exitAnimationDuration: 160,
    });
  });
});
