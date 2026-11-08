import { TaskPriority } from '../../shared/models/task-filters';

export { PRIORITIES, priorityLabel } from '../../shared/models/task-filters';
export type { TaskPriority } from '../../shared/models/task-filters';

export interface TaskPersonDto {
  userId: string;
  displayName: string;
}

/**
 * A task as its panel shows it, its assignees by name. canChange: a real member of an open project,
 * the task not archived.
 */
export interface TaskItemDto {
  id: string;
  projectId: string;
  projectName: string;
  key: string;
  number: number;
  title: string;
  description: string | null;
  priority: TaskPriority;
  dueDate: string | null;
  assignees: TaskPersonDto[];
  columnId: string;
  columnName: string;
  position: number;
  completedAt: string | null;
  archivedAt: string | null;
  createdAt: string;
  createdBy: TaskPersonDto | null;
  updatedAt: string | null;
  /** From the first uploaded (docs/SPEC.md §4.4). */
  attachments: AttachmentDto[];
  canChange: boolean;
}

/**
 * A file on a task. canDelete: whoever uploaded it, still a real member, or tasks.archive, on an open
 * task of an open project. Its size is in bytes.
 */
export interface AttachmentDto {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  uploadedBy: TaskPersonDto;
  uploadedAt: string;
  canDelete: boolean;
}

/**
 * A comment, in basic markdown. updatedAt is null until it's edited; canEdit: its author, still a real
 * member of an open project, on an open task.
 */
export interface CommentDto {
  id: string;
  taskId: string;
  author: TaskPersonDto;
  body: string;
  createdAt: string;
  updatedAt: string | null;
  canEdit: boolean;
}

/**
 * One change of a task (docs/SPEC.md §4.4 "Istoricul unui task"). The values are the entry's text: ids
 * for Assignees, sorted and separated by commas, and for ColumnId; names gives each of them its name,
 * null for a column deleted since. Dates are ISO, in UTC. In the list a Description entry has both
 * values null: GET /api/tasks/{id}/history/{entryId} gives its texts.
 */
export interface TaskHistoryEntryDto {
  id: string;
  field: string;
  oldValue: string | null;
  newValue: string | null;
  changedAt: string;
  changedBy: TaskPersonDto;
  names: Record<string, string | null>;
}

/** A row of the project's List view. Assignees come by name; updatedAt is the last change, or the creation. */
export interface TaskSummaryDto {
  id: string;
  key: string;
  number: number;
  title: string;
  columnId: string;
  columnName: string;
  columnIsDone: boolean;
  priority: TaskPriority;
  dueDate: string | null;
  assignees: TaskPersonDto[];
  completedAt: string | null;
  archivedAt: string | null;
  updatedAt: string;
}

/** What a task needs of its project: the board's columns, and whether the caller is in it. */
export interface TaskProjectDto {
  id: string;
  name: string;
  key: string;
  archivedAt: string | null;
  myRole: 'Owner' | 'Member' | null;
  canManage: boolean;
  columns: TaskColumnDto[];
}

export interface TaskColumnDto {
  id: string;
  name: string;
  position: number;
  isDone: boolean;
}

export interface TaskMemberDto {
  userId: string;
  email: string;
  displayName: string;
  role: 'Owner' | 'Member';
  isActive: boolean;
}

export interface NewTask {
  title: string;
  description: string | null;
  priority: TaskPriority;
  columnId?: string | null;
  assigneeIds?: string[];
  dueDate?: string | null;
}
