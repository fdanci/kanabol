import { ILogger, LogLevel } from '@microsoft/signalr';
import { Logger } from '../logging/logger';

/** The token goes in the hub's address (docs/SPEC.md §4.5), and no log keeps a token (§8.1). */
export function withoutToken(message: string): string {
  return message.replace(/access_token=[^&\s"']*/gi, 'access_token=[hidden]');
}

/**
 * What SignalR says goes through Logger, the only code that writes to the console, without the token.
 * A lost connection is a warning: SignalR tries again by itself, and so does Realtime after it.
 */
export class HubLog implements ILogger {
  constructor(private readonly logger: Logger) {}

  log(level: LogLevel, message: string): void {
    const said = `SignalR: ${withoutToken(message)}`;
    if (level >= LogLevel.Critical) {
      this.logger.error(said);
    } else if (level >= LogLevel.Warning) {
      this.logger.warn(said);
    } else {
      this.logger.debug(said);
    }
  }
}
