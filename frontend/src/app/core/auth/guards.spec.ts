import {
  ActivatedRouteSnapshot,
  RedirectCommand,
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
    if (result instanceof RedirectCommand) {
      return router.serializeUrl(result.redirectTo);
    }
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
    it('lets through someone with the permission', () => {
      auth.signedIn = true;
      auth.held.add('users.manage');

      expect(run(permissionGuard('users.manage'), '/users')).toBe(true);
    });

    // The address stays what they typed (docs/UI_UX_SPEC.md §6.8).
    it('shows the 403 page to someone without it, at the same address', () => {
      auth.signedIn = true;

      const result = run(permissionGuard('users.manage'), '/users');

      expect(result).toBeInstanceOf(RedirectCommand);
      expect(urlOf(result)).toBe('/forbidden');
      expect((result as RedirectCommand).navigationBehaviorOptions?.skipLocationChange).toBe(true);
    });
  });
});
