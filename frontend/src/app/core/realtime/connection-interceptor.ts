import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { isApiRequest } from '../api/api-client';
import { TabConnection } from './tab-connection';

export const CONNECTION_HEADER = 'X-Connection-Id';

/**
 * Tells the API which hub connection this tab has (docs/SPEC.md §9): the events a request causes carry
 * it as originConnectionId, and this tab's board ignores them, having changed itself already. Without a
 * connection the header stays off, and every board hears the change.
 */
export const connectionInterceptor: HttpInterceptorFn = (request, next) => {
  const connectionId = inject(TabConnection).id();
  if (
    !isApiRequest(request.url) ||
    connectionId === null ||
    request.headers.has(CONNECTION_HEADER)
  ) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { [CONNECTION_HEADER]: connectionId } }));
};
