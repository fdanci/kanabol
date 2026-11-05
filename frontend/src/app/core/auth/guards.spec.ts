import {
  ActivatedRouteSnapshot,
  PartialMatchRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { authGuard } from './auth-guard';
import { AuthStore } from './auth-store';
import { permissionGuard } from './permission-guard';

class FakeAuthStore {
  signedIn = false;
  mustChange = false;
  held = new Set<string>();
  isSignedIn = () => this.signedIn;
  mustChangePassword = () => this.mustChange;
  has = (permission: string) => this.held.has(permission);
}

describe('guards', () => {
  let auth: FakeAuthStore;
  let router: Router;

  beforeEach(() => {
    auth = new FakeAuthStore();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthStore, useValue: auth }],
    });
    router = TestBed.inject(Router);
  });

  function run(guard: typeof authGuard, url: string): ReturnType<typeof authGuard> {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  function urlOf(result: unknown): string {
    return router.serializeUrl(result as UrlTree);
  }

  describe('authGuard', () => {
    it('sends someone signed out to the login page, with where they were going', () => {
      expect(urlOf(run(authGuard, '/projects/7'))).toBe('/login?returnUrl=%2Fprojects%2F7');
    });

    it('lets someone signed in through', () => {
      auth.signedIn = true;

      expect(run(authGuard, '/projects/7')).toBe(true);
    });

    it('sends someone who has to change their password to that page first', () => {
      auth.signedIn = true;
      auth.mustChange = true;

      expect(urlOf(run(authGuard, '/projects/7'))).toBe('/change-password');
    });

    it('lets them change it', () => {
      auth.signedIn = true;
      auth.mustChange = true;

      expect(run(authGuard, '/change-password')).toBe(true);
    });

    it('sends someone with nothing to change from that page to the start', () => {
      auth.signedIn = true;

      expect(urlOf(run(authGuard, '/change-password'))).toBe('/');
    });
  });

  describe('permissionGuard', () => {
    function matches(permission: 'users.manage'): unknown {
      return TestBed.runInInjectionContext(() =>
        permissionGuard(permission)({}, [], {} as PartialMatchRouteSnapshot),
      );
    }

    it('matches for someone with the permission', () => {
      auth.signedIn = true;
      auth.held.add('users.manage');

      expect(matches('users.manage')).toBe(true);
    });

    // The router then takes the next route on the same path: the 403 page (docs/UI_UX_SPEC.md §6.8).
    it('does not match for someone without it', () => {
      auth.signedIn = true;

      expect(matches('users.manage')).toBe(false);
    });
  });
});
