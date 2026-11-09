import { Routes } from '@angular/router';
import { ReportsPage } from './reports-page';

/** /reports, for reports.view, which app.routes.ts asks for (docs/SPEC.md §5.1.1). */
export const REPORTS_ROUTES: Routes = [{ path: '', component: ReportsPage }];
