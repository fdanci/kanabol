import { Routes } from '@angular/router';
import { ProjectMembers } from './project-members';
import { ProjectPage } from './project-page';
import { ProjectSettings } from './project-settings';
import { ProjectsPage } from './projects-page';

/**
 * Loaded with the first visit to /projects. Any signed-in user may open it: the API answers with the
 * projects they can see, and refuses the rest.
 */
export const PROJECTS_ROUTES: Routes = [
  { path: '', component: ProjectsPage },
  {
    path: ':id',
    component: ProjectPage,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'board' },
      // The board and the list are other features': the projects feature only knows their routes
      // (docs/SPEC.md §6.1).
      {
        path: 'board',
        loadComponent: () => import('../board/board-page').then((page) => page.BoardPage),
      },
      {
        path: 'list',
        loadComponent: () => import('../tasks/task-list-page').then((page) => page.TaskListPage),
      },
      { path: 'members', component: ProjectMembers },
      // docs/SPEC.md §6.1: the one feature a route of another loads, the reports page with its project
      // fixed; it stays in its own chunk.
      {
        path: 'reports',
        loadComponent: () => import('../reports/reports-page').then((page) => page.ReportsPage),
        data: { projectFixed: true },
      },
      { path: 'settings', component: ProjectSettings },
    ],
  },
];
