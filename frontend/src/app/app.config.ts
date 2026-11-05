import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { correlationInterceptor } from './core/api/correlation-interceptor';
import { errorInterceptor } from './core/api/error-interceptor';
import { ThemeStore } from './core/layout/theme-store';
import { GlobalErrorHandler } from './core/logging/global-error-handler';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    provideRouter(routes),
    // The first one sees a request first and its answer last, so errorInterceptor only hears what's
    // left once the interceptors after it are done.
    provideHttpClient(withInterceptors([correlationInterceptor, errorInterceptor])),
    provideAppInitializer(() => {
      // Creating the store applies the saved theme, before anything renders.
      inject(ThemeStore);
    }),
  ],
};
