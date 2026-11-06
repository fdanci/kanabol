import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ClientLogs } from './client-logs';
import { IS_DEV_MODE, Logger } from './logger';

type Level = 'debug' | 'info' | 'warn' | 'error';

describe('Logger', () => {
  let written: Record<Level, ReturnType<typeof vi.spyOn>>;
  let sent: { add: ReturnType<typeof vi.fn> };

  function loggerIn(devMode: boolean): Logger {
    TestBed.configureTestingModule({
      providers: [
        { provide: IS_DEV_MODE, useValue: devMode },
        { provide: ClientLogs, useValue: sent },
      ],
    });
    return TestBed.inject(Logger);
  }

  beforeEach(() => {
    const silent = () => undefined;
    written = {
      debug: vi.spyOn(console, 'debug').mockImplementation(silent),
      info: vi.spyOn(console, 'info').mockImplementation(silent),
      warn: vi.spyOn(console, 'warn').mockImplementation(silent),
      error: vi.spyOn(console, 'error').mockImplementation(silent),
    };
    sent = { add: vi.fn() };
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('writes every level to the console in development, and sends nothing', () => {
    const logger = loggerIn(true);

    logger.debug('a', 1);
    logger.info('b');
    logger.warn('c');
    logger.error('d', new Error('e'));

    expect(written.debug).toHaveBeenCalledWith('a', 1);
    expect(written.info).toHaveBeenCalledWith('b');
    expect(written.warn).toHaveBeenCalledWith('c');
    expect(written.error).toHaveBeenCalledWith('d', expect.any(Error));
    expect(sent.add).not.toHaveBeenCalled();
  });

  it('keeps only warnings and errors in production', () => {
    const logger = loggerIn(false);

    logger.debug('a');
    logger.info('b');
    logger.warn('c');
    logger.error('d');

    expect(written.debug).not.toHaveBeenCalled();
    expect(written.info).not.toHaveBeenCalled();
    expect(written.warn).toHaveBeenCalledWith('c');
    expect(written.error).toHaveBeenCalledWith('d');
  });

  // docs/SPEC.md §8.3: to POST /api/client-logs, with the page and the request's correlation id.
  it('sends warnings and errors to the API in production, with the page they happened on', () => {
    const logger = loggerIn(false);

    logger.warn('Slow board');
    logger.debug('not this');

    expect(sent.add).toHaveBeenCalledTimes(1);
    expect(sent.add).toHaveBeenCalledWith({
      level: 'warn',
      message: 'Slow board',
      url: document.location.href,
    });
  });

  it('sends an error with its stack', () => {
    const logger = loggerIn(false);
    const error = new TypeError('x is undefined');

    logger.error('Uncaught error', error);

    const [entry] = sent.add.mock.calls[0] as [{ level: string; message: string; details: string }];
    expect(entry.level).toBe('error');
    expect(entry.message).toBe('Uncaught error');
    expect(entry.details).toContain('TypeError: x is undefined');
    expect(entry.details).toContain(error.stack!.split('\n')[1].trim());
  });

  it('sends a failed request with its status and the correlation id it went with', () => {
    const logger = loggerIn(false);
    const failed = new HttpErrorResponse({
      status: 500,
      url: '/api/users',
      error: { status: 500, correlationId: 'from-the-body' },
      headers: new HttpHeaders({ 'X-Correlation-Id': 'from-the-header' }),
    });

    logger.error('Uncaught error', failed);

    expect(sent.add).toHaveBeenCalledWith(
      expect.objectContaining({
        correlationId: 'from-the-body',
        details: expect.stringContaining('500') as unknown,
      }),
    );
  });

  it('takes the correlation id from the header when the body has none', () => {
    const logger = loggerIn(false);

    logger.error(
      'Uncaught error',
      new HttpErrorResponse({
        status: 502,
        url: '/api/users',
        error: 'Bad gateway',
        headers: new HttpHeaders({ 'X-Correlation-Id': 'from-the-header' }),
      }),
    );

    expect(sent.add).toHaveBeenCalledWith(
      expect.objectContaining({ correlationId: 'from-the-header' }),
    );
  });

  it('writes to the console even when sending fails', () => {
    sent.add.mockImplementation(() => {
      throw new Error('no network');
    });
    const logger = loggerIn(false);

    expect(() => {
      logger.error('d');
    }).not.toThrow();
    expect(written.error).toHaveBeenCalledWith('d');
  });
});
