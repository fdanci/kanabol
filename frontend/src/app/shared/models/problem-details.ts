import { HttpErrorResponse } from '@angular/common/http';

/** What the API answers with when it refuses or fails (docs/SPEC.md §4.6). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  correlationId?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** The ProblemDetails in a failed response, if its body is one. */
export function problemIn(error: HttpErrorResponse): ProblemDetails | null {
  const body: unknown = error.error;
  return typeof body === 'object' && body !== null && !(body instanceof ProgressEvent)
    ? body
    : null;
}
