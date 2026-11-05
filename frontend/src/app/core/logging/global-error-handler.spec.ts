import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Snackbar } from '../../shared/ui/snackbar';
import { GlobalErrorHandler } from './global-error-handler';
import { Logger } from './logger';

describe('GlobalErrorHandler', () => {
  let logger: { error: ReturnType<typeof vi.fn> };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let handler: GlobalErrorHandler;

  beforeEach(() => {
    logger = { error: vi.fn() };
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        GlobalErrorHandler,
        { provide: Logger, useValue: logger },
        { provide: Snackbar, useValue: snackbar },
      ],
    });
    handler = TestBed.inject(GlobalErrorHandler);
  });

  it('logs an uncaught error and tells the user something went wrong', () => {
    const error = new Error('boom');

    handler.handleError(error);

    expect(logger.error).toHaveBeenCalledWith('Uncaught error', error);
    expect(snackbar.error).toHaveBeenCalledWith(
      'A apărut o eroare neașteptată. Reîncarcă pagina și reîncearcă.',
      null,
    );
  });

  // errorInterceptor showed it already, with its code.
  it('only logs a failed request nobody caught', () => {
    const error = new HttpErrorResponse({ status: 500, url: '/api/x' });

    handler.handleError(error);

    expect(logger.error).toHaveBeenCalledWith('Uncaught error', error);
    expect(snackbar.error).not.toHaveBeenCalled();
  });
});
