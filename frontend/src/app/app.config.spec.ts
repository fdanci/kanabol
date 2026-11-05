import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus, ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from './app.config';
import { GlobalErrorHandler } from './core/logging/global-error-handler';
import { Snackbar } from './shared/ui/snackbar';

describe('appConfig', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    delete root.dataset['theme'];
    // jsdom doesn't have it.
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: vi.fn() }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('applies the saved theme before the app starts', async () => {
    localStorage.setItem('kb.theme', 'dark');

    TestBed.configureTestingModule({ providers: appConfig.providers });
    await TestBed.inject(ApplicationInitStatus).donePromise;

    expect(root.dataset['theme']).toBe('dark');
  });

  it('sends API requests with an id, and shows what fails', () => {
    const snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: Snackbar, useValue: snackbar },
      ],
    });

    TestBed.inject(HttpClient)
      .get('/api/users')
      .subscribe({ error: () => undefined });
    const request = TestBed.inject(HttpTestingController).expectOne('/api/users');
    request.flush({ status: 500 }, { status: 500, statusText: 'Error' });

    expect(request.request.headers.has('X-Correlation-Id')).toBe(true);
    expect(snackbar.error).toHaveBeenCalled();
  });

  it('hands uncaught errors to GlobalErrorHandler', () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });

    expect(TestBed.inject(ErrorHandler)).toBeInstanceOf(GlobalErrorHandler);
  });
});
