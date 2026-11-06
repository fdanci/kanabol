import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { QUIET } from '../api/error-interceptor';
import { AuthStore } from '../auth/auth-store';

/** One warning or error for POST /api/client-logs (docs/SPEC.md §8.3). */
export interface ClientLogEntry {
  level: 'warn' | 'error';
  message: string;
  /** The page it happened on. */
  url: string;
  /** The id of the API request it's about, which Seq shows the backend's lines for. */
  correlationId?: string;
  /** The error itself: its stack, or the status of a failed request. */
  details?: string;
}

/** At most this many entries leave a tab in a minute; the rest are only counted (§8.3). */
export const CLIENT_LOG_LIMIT = 10;
/** How long entries gather before they leave together. */
export const CLIENT_LOG_DELAY_MS = 5_000;
const MINUTE_MS = 60_000;

/** The lengths the API takes (RecordClientLogsCommandValidator): past them it'd refuse the batch. */
const LIMITS = { message: 4_000, url: 2_000, details: 16_000, correlationId: 100 } as const;

function cut(entry: ClientLogEntry): ClientLogEntry {
  const shortened: ClientLogEntry = {
    ...entry,
    message: entry.message.slice(0, LIMITS.message),
    url: entry.url.slice(0, LIMITS.url),
  };
  if (entry.details !== undefined) {
    shortened.details = entry.details.slice(0, LIMITS.details);
  }
  if (entry.correlationId !== undefined) {
    shortened.correlationId = entry.correlationId.slice(0, LIMITS.correlationId);
  }
  return shortened;
}

/**
 * Sends the browser's warnings and errors to the API, for Seq to show next to the backend's. Entries
 * logged within a few seconds leave in one batch, at most CLIENT_LOG_LIMIT a minute, and the next
 * batch says how many were dropped. Nothing leaves without a session: the request would fail, and
 * its 401 would send the user to the login page.
 */
@Injectable({ providedIn: 'root' })
export class ClientLogs {
  private readonly api = inject(ApiClient);
  private readonly auth = inject(AuthStore);
  private queue: ClientLogEntry[] = [];
  private acceptedAt: number[] = [];
  private dropped = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;

  add(entry: ClientLogEntry): void {
    if (!this.canSend()) {
      return;
    }
    const now = Date.now();
    this.acceptedAt = this.acceptedAt.filter((time) => now - time < MINUTE_MS);
    if (this.acceptedAt.length >= CLIENT_LOG_LIMIT) {
      this.dropped++;
      return;
    }

    this.acceptedAt.push(now);
    this.queue.push(cut(entry));
    this.timer ??= setTimeout(() => {
      this.send();
    }, CLIENT_LOG_DELAY_MS);
  }

  private canSend(): boolean {
    return this.auth.isSignedIn() && !this.auth.mustChangePassword();
  }

  private send(): void {
    this.timer = null;
    const entries = this.queue;
    const dropped = this.dropped;
    this.queue = [];
    this.dropped = 0;
    if (entries.length === 0 || !this.canSend()) {
      return;
    }

    // Quiet: a log that can't be sent shows no snackbar, and logs nothing that would try again.
    this.api
      .post<null>(
        '/client-logs',
        { entries, dropped },
        { context: new HttpContext().set(QUIET, true) },
      )
      .subscribe({ error: () => undefined });
  }
}
