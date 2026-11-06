import { Routes } from '@angular/router';
import { RolesPage } from './roles-page';

/** Loaded with the first visit to /roles. app.routes.ts checks roles.manage before it. */
export const ROLES_ROUTES: Routes = [{ path: '', component: RolesPage }];
