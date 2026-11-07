/**
 * What the board hub sends (docs/SPEC.md §9): small payloads, which say what changed and where, not the
 * task itself. originConnectionId is the connection of the tab that made the change, or null.
 */
export interface TaskMessage {
  taskId: string;
  projectId: string;
  byUserId: string;
  originConnectionId: string | null;
}

/** Where the task went: the board moves the card itself, with its animation. */
export interface TaskMovedMessage extends TaskMessage {
  columnId: string;
  /** From 0 at the top of the column, as the API numbers it. */
  position: number;
}

export interface TaskUpdatedMessage extends TaskMessage {
  field: 'Title' | 'Description' | 'Priority' | 'DueDate' | 'Assignees';
}

/** On the user's own group, for the bell (group 17a). */
export interface NotificationCreatedMessage {
  notificationId: string;
}

/** Each event the hub sends, by name, with its payload. CommentAdded comes with group 15. */
export interface BoardEvents {
  TaskCreated: TaskMessage;
  TaskMoved: TaskMovedMessage;
  TaskUpdated: TaskUpdatedMessage;
  TaskArchived: TaskMessage;
  TaskRestored: TaskMessage;
  NotificationCreated: NotificationCreatedMessage;
}

export type BoardEvent = keyof BoardEvents;
