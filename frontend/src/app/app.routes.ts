import { Route, Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth-guard';
import { permissionGuard } from './core/auth/permission-guard';
import { PERMISSIONS, Permission } from './core/auth/permissions';
import { ForbiddenPage, NotFoundPage, PlaceholderPage } from './core/layout/pages';
import { Shell } from './core/layout/shell';

/** A page whose group hasn't been built yet: the sidebar already links to it. */
function placeholder(path: string, title: string): Route {
  return { path, title: `${title} · Kanabol`, component: PlaceholderPage, data: { title } };
}

/**
 * A page that needs a permission. Without it, the route after it on the same path matches instead,
 * and shows the 403 page at the address asked for (docs/UI_UX_SPEC.md §6.8).
 */
function withPermission(permission: Permission, route: Route): Route[] {
  return [
    { ...route, canMatch: [permissionGuard(permission)] },
    { path: route.path, title: $localize`Fără acces · Kanabol`, component: ForbiddenPage },
  ];
}

export const routes: Routes = [
  {
    path: 'login',
    title: $localize`Intră în cont · Kanabol`,
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((page) => page.LoginPage),
  },
  {
    path: 'change-password',
    title: $localize`Schimbă parola · Kanabol`,
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/auth/change-password-page').then((page) => page.ChangePasswordPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      placeholder('', $localize`Panou principal`),
      placeholder('projects', $localize`Proiectele mele`),
      placeholder('my-tasks', $localize`Task-urile mele`),
      placeholder('notifications', $localize`Notificări`),
      ...withPermission(PERMISSIONS.usersManage, placeholder('users', $localize`Utilizatori`)),
      ...withPermission(PERMISSIONS.rolesManage, placeholder('roles', $localize`Roluri`)),
      ...withPermission(PERMISSIONS.reportsView, placeholder('reports', $localize`Rapoarte`)),
      { path: '**', title: $localize`Pagina nu există · Kanabol`, component: NotFoundPage },
    ],
  },
];
