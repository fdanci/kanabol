import { NotificationDto, NotificationType } from './models';

/**
 * What a notification says, made in the UI from its type and its task, without who did it
 * (docs/UI_UX_SPEC.md §7.6). One without a task, or of a type a later version adds, shows the API's
 * message as it is.
 */
export function notificationText(notification: NotificationDto): string {
  const task = notification.task;
  if (!task) {
    return notification.message;
  }
  const subject = `${task.key}: ${task.title}`;
  switch (notification.type) {
    case 'TaskOverdue':
      return $localize`${subject}:subject: a depășit deadline-ul`;
    case 'TaskAssigned':
      return $localize`Ai fost asignat pe ${subject}:subject:`;
    case 'CommentAdded':
      return $localize`Comentariu nou pe ${subject}:subject:`;
    default:
      return notification.message;
  }
}

const ICONS: Record<NotificationType, string> = {
  TaskOverdue: 'event_busy',
  TaskAssigned: 'person_add',
  CommentAdded: 'chat_bubble',
};

/** The type's icon (UI spec §7.6), the bell's own for a type it doesn't know. */
export function notificationIcon(type: NotificationType): string {
  return (ICONS as Partial<Record<string, string>>)[type] ?? 'notifications';
}

/** The type as the page's select names it. */
export function notificationTypeLabel(type: NotificationType): string {
  switch (type) {
    case 'TaskOverdue':
      return $localize`Deadline`;
    case 'TaskAssigned':
      return $localize`Asignări`;
    case 'CommentAdded':
      return $localize`Comentarii`;
  }
}

/** The badge's number: none at 0, "99+" over 99. */
export function badgeText(count: number): string {
  if (count <= 0) {
    return '';
  }
  return count > 99 ? '99+' : String(count);
}
