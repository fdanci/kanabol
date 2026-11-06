import { Routes } from '@angular/router';
import { ProjectMembers } from './project-members';
import { ProjectPage } from './project-page';
import { ProjectPlaceholder } from './project-placeholder';
import { ProjectSettings } from './project-settings';
import { ProjectsPage } from './projects-page';

/**
 * Loaded with the first visit to /projects. Any signed-in user may open it: the API answers with the
 * projects they can see, and refuses the rest. The reports come later.
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
      { path: 'reports', component: ProjectPlaceholder },
      { path: 'settings', component: ProjectSettings },
    ],
  },
];
