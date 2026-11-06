import { TestBed } from '@angular/core/testing';
import { IS_DEV_MODE, Logger } from './logger';

type Level = 'debug' | 'info' | 'warn' | 'error';

function loggerIn(devMode: boolean): Logger {
  TestBed.configureTestingModule({ providers: [{ provide: IS_DEV_MODE, useValue: devMode }] });
  return TestBed.inject(Logger);
}

describe('Logger', () => {
  let written: Record<Level, ReturnType<typeof vi.spyOn>>;

  beforeEach(() => {
    const silent = () => undefined;
    written = {
      debug: vi.spyOn(console, 'debug').mockImplementation(silent),
      info: vi.spyOn(console, 'info').mockImplementation(silent),
      warn: vi.spyOn(console, 'warn').mockImplementation(silent),
      error: vi.spyOn(console, 'error').mockImplementation(silent),
    };
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('writes every level to the console in development', () => {
    const logger = loggerIn(true);

    logger.debug('a', 1);
    logger.info('b');
    logger.warn('c');
    logger.error('d', new Error('e'));

    expect(written.debug).toHaveBeenCalledWith('a', 1);
    expect(written.info).toHaveBeenCalledWith('b');
    expect(written.warn).toHaveBeenCalledWith('c');
    expect(written.error).toHaveBeenCalledWith('d', expect.any(Error));
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
});
