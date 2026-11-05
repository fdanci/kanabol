import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { AuthStore } from './auth-store';
import { Permission } from './permissions';

/**
 * Opens a page only with its permission. Without it, the 403 page shows at the address typed
 * (docs/UI_UX_SPEC.md §6.8). It only spares a refusal: the API checks the permission anyway.
 */
export function permissionGuard(permission: Permission): CanActivateFn {
  return () => {
    if (inject(AuthStore).has(permission)) {
      return true;
    }
    return new RedirectCommand(inject(Router).parseUrl('/forbidden'), { skipLocationChange: true });
  };
}
