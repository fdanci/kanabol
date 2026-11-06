import { Routes } from '@angular/router';
import { ProjectMembers } from './project-members';
import { ProjectPage } from './project-page';
import { ProjectPlaceholder } from './project-placeholder';
import { ProjectSettings } from './project-settings';
import { ProjectsPage } from './projects-page';

/**
 * Loaded with the first visit to /projects. Any signed-in user may open it: the API answers with the
 * projects they can see, and refuses the rest. The board, the list and the reports come later.
 */
export const PROJECTS_ROUTES: Routes = [
  { path: '', component: ProjectsPage },
  {
    path: ':id',
    component: ProjectPage,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'board' },
      { path: 'board', component: ProjectPlaceholder },
      { path: 'list', component: ProjectPlaceholder },
      { path: 'members', component: ProjectMembers },
      { path: 'reports', component: ProjectPlaceholder },
      { path: 'settings', component: ProjectSettings },
    ],
  },
];
