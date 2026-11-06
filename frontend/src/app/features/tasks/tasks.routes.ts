import { Routes } from '@angular/router';
import { TaskLinkPage } from './task-link-page';

/** /tasks/KAN-12 opens the task over its project's board. The panel itself is the panel outlet's. */
export const TASKS_ROUTES: Routes = [{ path: ':key', component: TaskLinkPage }];
