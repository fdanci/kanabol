import { Injectable, signal } from '@angular/core';

/**
 * This tab's hub connection id, null without one: Realtime sets it, connectionInterceptor sends it. Apart
 * from Realtime, so the API's requests don't pull SignalR's code into the first bundle: only the board does.
 */
@Injectable({ providedIn: 'root' })
export class TabConnection {
  readonly id = signal<string | null>(null);
}
