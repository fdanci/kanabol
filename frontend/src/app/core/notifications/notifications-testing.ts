import { NotificationDto } from './models';

/** A notification for the specs, unread, on KAN-12 "Login SSO" a minute ago. A plain file, so importing it runs no spec. */
export function aNotification(extra: Partial<NotificationDto> = {}): NotificationDto {
  return {
    id: 'n1',
    type: 'TaskOverdue',
    message: 'KAN-12: Login SSO is overdue',
    createdAt: new Date(Date.now() - 60_000).toISOString(),
    readAt: null,
    task: { id: 't12', key: 'KAN-12', title: 'Login SSO' },
    ...extra,
  };
}
