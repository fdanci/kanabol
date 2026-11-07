import { TaskPriority } from '../../shared/models/task-filters';

export { PRIORITIES, priorityLabel } from '../../shared/models/task-filters';
export type { TaskPriority } from '../../shared/models/task-filters';

export interface TaskPersonDto {
  userId: string;
  displayName: string;
}

/** A task as its panel shows it. canChange: a real member of an open project, the task not archived. */
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
  assignee: TaskPersonDto | null;
  columnId: string;
  columnName: string;
  position: number;
  completedAt: string | null;
  archivedAt: string | null;
  createdAt: string;
  createdBy: TaskPersonDto | null;
  updatedAt: string | null;
  canChange: boolean;
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
  assigneeId?: string | null;
  dueDate?: string | null;
}
