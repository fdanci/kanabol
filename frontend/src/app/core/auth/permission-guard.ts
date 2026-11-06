import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';
import { AuthStore } from './auth-store';
import { Permission } from './permissions';

/**
 * Matches a page's route only with its permission. Without it the router goes on to the next route
 * with the same path, which shows the 403 page at the address asked for (docs/UI_UX_SPEC.md §6.8).
 * It only spares a refusal: the API checks the permission anyway.
 */
export function permissionGuard(permission: Permission): CanMatchFn {
  return () => inject(AuthStore).has(permission);
}
