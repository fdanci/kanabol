import { LogLevel } from '@microsoft/signalr';
import { Logger } from '../logging/logger';
import { HubLog, withoutToken } from './hub-log';

/** docs/SPEC.md §8.1: no log keeps a token, and SignalR writes the hub's address, token and all. */
describe('HubLog', () => {
  const logger = { debug: vi.fn(), warn: vi.fn(), error: vi.fn() };
  const log = new HubLog(logger as unknown as Logger);

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('hides the token in an address', () => {
    expect(
      withoutToken(
        'WebSocket connected to ws://localhost/hubs/board?id=abc&access_token=eyJ.eyJ.c2ln&x=1.',
      ),
    ).toBe('WebSocket connected to ws://localhost/hubs/board?id=abc&access_token=[hidden]&x=1.');
  });

  it('passes warnings and errors on as warnings, without the token, and critical ones as errors', () => {
    log.log(LogLevel.Warning, 'Retrying ws://h/hubs/board?access_token=secret');
    log.log(LogLevel.Error, 'Connection disconnected with error');
    log.log(LogLevel.Critical, 'Broken');

    expect(logger.warn.mock.calls).toEqual([
      ['SignalR: Retrying ws://h/hubs/board?access_token=[hidden]'],
      ['SignalR: Connection disconnected with error'],
    ]);
    expect(logger.error).toHaveBeenCalledWith('SignalR: Broken');
  });

  it('keeps the rest for debugging', () => {
    log.log(LogLevel.Information, 'WebSocket connected to ws://h/hubs/board?access_token=secret');

    expect(logger.debug).toHaveBeenCalledWith(
      'SignalR: WebSocket connected to ws://h/hubs/board?access_token=[hidden]',
    );
    expect(logger.warn).not.toHaveBeenCalled();
  });
});
