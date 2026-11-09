import { Route, Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth-guard';
import { permissionGuard } from './core/auth/permission-guard';
import { PERMISSIONS, Permission } from './core/auth/permissions';
import { ForbiddenPage, NotFoundPage } from './core/layout/pages';
import { Shell } from './core/layout/shell';
import { TASK_PANEL_OUTLET } from './shared/task-panel';

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
      {
        path: '',
        pathMatch: 'full',
        title: $localize`Panou principal · Kanabol`,
        loadChildren: () =>
          import('./features/dashboard/dashboard.routes').then(
            (dashboard) => dashboard.DASHBOARD_ROUTES,
          ),
      },
      {
        path: 'projects',
        title: $localize`Proiecte · Kanabol`,
        loadChildren: () =>
          import('./features/projects/projects.routes').then(
            (projects) => projects.PROJECTS_ROUTES,
          ),
      },
      {
        path: 'tasks',
        title: $localize`Task · Kanabol`,
        loadChildren: () =>
          import('./features/tasks/tasks.routes').then((tasks) => tasks.TASKS_ROUTES),
      },
      // A task's panel, over whatever page it was opened from: /projects/p1/list(panel:task/KAN-12).
      {
        path: 'task/:key',
        outlet: TASK_PANEL_OUTLET,
        loadComponent: () =>
          import('./features/tasks/task-panel-route').then((panel) => panel.TaskPanelRoute),
      },
      {
        path: 'my-tasks',
        title: $localize`Task-urile mele · Kanabol`,
        loadComponent: () =>
          import('./features/tasks/my-tasks-page').then((page) => page.MyTasksPage),
      },
      {
        path: 'notifications',
        title: $localize`Notificări · Kanabol`,
        loadChildren: () =>
          import('./features/notifications/notifications.routes').then(
            (notifications) => notifications.NOTIFICATIONS_ROUTES,
          ),
      },
      ...withPermission(PERMISSIONS.usersManage, {
        path: 'users',
        title: $localize`Utilizatori · Kanabol`,
        loadChildren: () =>
          import('./features/users/users.routes').then((users) => users.USERS_ROUTES),
      }),
      ...withPermission(PERMISSIONS.rolesManage, {
        path: 'roles',
        title: $localize`Roluri · Kanabol`,
        loadChildren: () =>
          import('./features/roles/roles.routes').then((roles) => roles.ROLES_ROUTES),
      }),
      ...withPermission(PERMISSIONS.reportsView, {
        path: 'reports',
        title: $localize`Rapoarte · Kanabol`,
        loadChildren: () =>
          import('./features/reports/reports.routes').then((reports) => reports.REPORTS_ROUTES),
      }),
      { path: '**', title: $localize`Pagina nu există · Kanabol`, component: NotFoundPage },
    ],
  },
];
