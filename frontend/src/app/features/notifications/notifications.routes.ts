import { Routes } from '@angular/router';
import { NotificationsPage } from './notifications-page';

/** /notifications, for anyone signed in: the API gives each user their own (docs/SPEC.md §4.4). */
export const NOTIFICATIONS_ROUTES: Routes = [{ path: '', component: NotificationsPage }];
