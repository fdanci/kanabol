import { Routes } from '@angular/router';
import { DashboardPage } from './dashboard-page';

/** /, the main panel, for anyone signed in: the API gives each user their own (docs/SPEC.md §4.4). */
export const DASHBOARD_ROUTES: Routes = [{ path: '', component: DashboardPage }];
