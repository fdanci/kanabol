import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from './app.config';

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
});
