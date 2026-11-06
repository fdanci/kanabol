/* eslint-disable no-console -- the one place that writes to the console (docs/SPEC.md §8.3). */
import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, InjectionToken, Injector, inject, isDevMode } from '@angular/core';
import { problemIn } from '../../shared/models/problem-details';
import { CORRELATION_HEADER } from '../api/correlation-interceptor';
import { ClientLogEntry, ClientLogs } from './client-logs';

export const IS_DEV_MODE = new InjectionToken<boolean>('IS_DEV_MODE', {
  providedIn: 'root',
  factory: () => isDevMode(),
});

/** What the details of a log line say, as text: an error's stack, a failed request's status. */
function describe(detail: unknown): string {
  if (detail instanceof HttpErrorResponse) {
    return `HTTP ${String(detail.status)} ${detail.url ?? ''}`.trim();
  }
  if (detail instanceof Error) {
    return detail.stack?.includes(detail.message)
      ? detail.stack
      : `${detail.name}: ${detail.message}\n${detail.stack ?? ''}`;
  }
  if (typeof detail === 'string') {
    return detail;
  }
  try {
    return JSON.stringify(detail);
  } catch {
    return String(detail);
  }
}

/** The correlation id of the first failed request among the details, as the snackbar showed it. */
function correlationIn(details: unknown[]): string | undefined {
  const failed = details.find((detail) => detail instanceof HttpErrorResponse);
  if (!failed) {
    return undefined;
  }
  return problemIn(failed)?.correlationId ?? failed.headers.get(CORRELATION_HEADER) ?? undefined;
}

/**
 * Everything the app logs goes through here. In development it all goes to the console. In
 * production only warnings and errors stay, and they also go to POST /api/client-logs, for Seq.
 */
@Injectable({ providedIn: 'root' })
export class Logger {
  private readonly devMode = inject(IS_DEV_MODE);
  // Asked for when first needed: ClientLogs reaches HttpClient, which the error handler mustn't wait on.
  private readonly injector = inject(Injector);

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
    this.send('warn', message, details);
  }

  error(message: string, ...details: unknown[]): void {
    console.error(message, ...details);
    this.send('error', message, details);
  }

  private send(level: ClientLogEntry['level'], message: string, details: unknown[]): void {
    if (this.devMode) {
      return;
    }
    try {
      const entry: ClientLogEntry = { level, message, url: document.location.href };
      if (details.length > 0) {
        entry.details = details.map(describe).join('\n');
      }
      const correlationId = correlationIn(details);
      if (correlationId) {
        entry.correlationId = correlationId;
      }
      this.injector.get(ClientLogs).add(entry);
    } catch {
      // Logging must never throw: the console has the line already.
    }
  }
}
