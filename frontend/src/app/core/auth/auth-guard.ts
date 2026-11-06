import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth-store';

export const CHANGE_PASSWORD_URL = '/change-password';

/**
 * Signed in, and past a forced password change: until then, the API only opens that change and
 * GET /api/auth/me (docs/SPEC.md §4.5), so the app only opens its page.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  const changingPassword = state.url.startsWith(CHANGE_PASSWORD_URL);

  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  if (auth.mustChangePassword() !== changingPassword) {
    return router.createUrlTree([auth.mustChangePassword() ? CHANGE_PASSWORD_URL : '/']);
  }
  return true;
};

/** The login page is for someone signed out: anyone else goes to the start. */
export const guestGuard: CanActivateFn = () =>
  !inject(AuthStore).isSignedIn() || inject(Router).createUrlTree(['/']);
