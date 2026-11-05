import { HttpErrorResponse } from '@angular/common/http';
import { ErrorHandler, Injectable, inject } from '@angular/core';
import { Snackbar } from '../../shared/ui/snackbar';
import { Logger } from './logger';

/** What nobody caught: it's logged, and the user hears that something went wrong (docs/SPEC.md §8.3). */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly logger = inject(Logger);
  private readonly snackbar = inject(Snackbar);

  handleError(error: unknown): void {
    this.logger.error('Uncaught error', error);

    // errorInterceptor has shown it already, with its code.
    if (!(error instanceof HttpErrorResponse)) {
      this.snackbar.error(
        $localize`A apărut o eroare neașteptată. Reîncarcă pagina și reîncearcă.`,
        null,
      );
    }
  }
}
