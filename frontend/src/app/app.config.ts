import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import localeRo from '@angular/common/locales/ro';
import {
  ApplicationConfig,
  ErrorHandler,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { MAT_DIALOG_DEFAULT_OPTIONS, MatDialogConfig } from '@angular/material/dialog';
import { MatIconRegistry } from '@angular/material/icon';
import { MatPaginatorIntl } from '@angular/material/paginator';
import {
  MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS,
  MatSlideToggleDefaultOptions,
} from '@angular/material/slide-toggle';
import { MAT_TOOLTIP_DEFAULT_OPTIONS, MatTooltipDefaultOptions } from '@angular/material/tooltip';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { correlationInterceptor } from './core/api/correlation-interceptor';
import { errorInterceptor } from './core/api/error-interceptor';
import { authInterceptor } from './core/auth/auth-interceptor';
import { AuthStore } from './core/auth/auth-store';
import { ThemeStore } from './core/layout/theme-store';
import { GlobalErrorHandler } from './core/logging/global-error-handler';
import { connectionInterceptor } from './core/realtime/connection-interceptor';
import { PaginatorLabels } from './shared/ui/paginator-labels';

// Dates as 04.11.2026 and numbers as 1.234,5 (docs/UI_UX_SPEC.md §11).
registerLocaleData(localeRo, 'ro-RO');

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    { provide: LOCALE_ID, useValue: 'ro-RO' },
    provideRouter(routes),
    // How the controls behave, where their shape in styles.scss can't say it (docs/UI_UX_SPEC.md §6.0).
    {
      provide: MAT_TOOLTIP_DEFAULT_OPTIONS,
      useValue: {
        showDelay: 500,
        hideDelay: 0,
        touchendHideDelay: 1500,
      } satisfies MatTooltipDefaultOptions,
    },
    {
      provide: MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS,
      useValue: { hideIcon: true } satisfies MatSlideToggleDefaultOptions,
    },
    {
      provide: MAT_DIALOG_DEFAULT_OPTIONS,
      useValue: {
        panelClass: 'kb-sheet',
        backdropClass: 'kb-sheet-backdrop',
        position: { top: '12vh' },
        maxWidth: '92vw',
        enterAnimationDuration: 240,
        exitAnimationDuration: 160,
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        hasBackdrop: true,
      } satisfies MatDialogConfig,
    },
    { provide: MatPaginatorIntl, useClass: PaginatorLabels },
    // The first one sees a request first and its answer last, so errorInterceptor only hears what's
    // left once the interceptors after it are done.
    // XHR, not fetch: only XHR reports how far an upload went (docs/UI_UX_SPEC.md §6.3).
    provideHttpClient(
      withXhr(),
      withInterceptors([
        correlationInterceptor,
        connectionInterceptor,
        errorInterceptor,
        authInterceptor,
      ]),
    ),
    provideAppInitializer(() => {
      // Creating the store applies the saved theme, before anything renders.
      inject(ThemeStore);
    }),
    // <mat-icon> draws with the self-hosted Material Symbols, so Material sizes it by its tokens (UI §12).
    provideAppInitializer(() => {
      inject(MatIconRegistry).setDefaultFontSetClass('kb-symbols');
    }),
    // The guards decide on the first page, so the session comes back from its cookie before it.
    provideAppInitializer(() => inject(AuthStore).restore()),
  ],
};
