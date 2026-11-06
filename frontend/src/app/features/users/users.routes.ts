import { Routes } from '@angular/router';
import { UsersPage } from './users-page';

/** Loaded with the first visit to /users. app.routes.ts checks users.manage before it. */
export const USERS_ROUTES: Routes = [{ path: '', component: UsersPage }];
