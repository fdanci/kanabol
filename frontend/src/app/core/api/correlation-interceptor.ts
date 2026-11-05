import { HttpInterceptorFn } from '@angular/common/http';
import { isApiRequest } from './api-client';

export const CORRELATION_HEADER = 'X-Correlation-Id';

/**
 * Gives each API request an id, which the API logs with everything the request does and sends back
 * in ProblemDetails. The error snackbar shows its end, so a user can quote it (docs/SPEC.md §8.2).
 */
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiRequest(request.url) || request.headers.has(CORRELATION_HEADER)) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { [CORRELATION_HEADER]: crypto.randomUUID() } }));
};
