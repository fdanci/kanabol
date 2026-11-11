import { Routes } from '@angular/router';
import { SettingsPage } from './settings-page';

/** /settings, for settings.manage (docs/UI_UX_SPEC.md §7.9): app.routes.ts holds the permission. */
export const SETTINGS_ROUTES: Routes = [{ path: '', component: SettingsPage }];
