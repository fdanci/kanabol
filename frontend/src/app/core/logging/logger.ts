/* eslint-disable no-console -- the one place that writes to the console (docs/SPEC.md §8.3). */
import { Injectable, InjectionToken, inject, isDevMode } from '@angular/core';

export const IS_DEV_MODE = new InjectionToken<boolean>('IS_DEV_MODE', {
  providedIn: 'root',
  factory: () => isDevMode(),
});

/**
 * Everything the app logs goes through here. In development it all goes to the console. In production
 * only warnings and errors stay, and POST /api/client-logs will send them to Seq once it exists.
 */
@Injectable({ providedIn: 'root' })
export class Logger {
  private readonly devMode = inject(IS_DEV_MODE);

  debug(message: string, ...details: unknown[]): void {
    if (this.devMode) {
      console.debug(message, ...details);
    }
  }

  info(message: string, ...details: unknown[]): void {
    if (this.devMode) {
      console.info(message, ...details);
    }
  }

  warn(message: string, ...details: unknown[]): void {
    console.warn(message, ...details);
  }

  error(message: string, ...details: unknown[]): void {
    console.error(message, ...details);
  }
}
