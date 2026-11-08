/** docs/SPEC.md §4.4 "Notificările": what GET /api/notifications gives, newest first. */
export type NotificationType = 'TaskOverdue' | 'TaskAssigned' | 'CommentAdded';

export const NOTIFICATION_TYPES: readonly NotificationType[] = [
  'TaskOverdue',
  'TaskAssigned',
  'CommentAdded',
];

/** The task as it is now, its title included; null for a notification without a task. */
export interface NotificationTaskDto {
  id: string;
  key: string;
  title: string;
}

export interface NotificationDto {
  id: string;
  type: NotificationType;
  /** The API's English text, which the UI shows only for a notification without a task. */
  message: string;
  createdAt: string;
  readAt: string | null;
  task: NotificationTaskDto | null;
}

/** unread-count, and how many read-all marked. */
export interface NotificationCountDto {
  count: number;
}

export interface NotificationQuery {
  page: number;
  pageSize: number;
  unread: boolean;
  type: NotificationType | null;
}
