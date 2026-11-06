import { TaskPriority } from '../../shared/models/task-filters';

export type { TaskPriority } from '../../shared/models/task-filters';

export interface BoardPersonDto {
  userId: string;
  displayName: string;
}

export interface BoardColumnDto {
  id: string;
  name: string;
  /** From 0 on the left, with no gaps. */
  position: number;
  /** A task in it is completed (docs/SPEC.md §5.1). */
  isDone: boolean;
}

/** A card: what docs/UI_UX_SPEC.md §7.4 puts on it, and its place. */
export interface BoardTaskDto {
  id: string;
  key: string;
  number: number;
  title: string;
  columnId: string;
  /** From 0 at the top of its column, with no gaps. */
  position: number;
  priority: TaskPriority;
  dueDate: string | null;
  assignee: BoardPersonDto | null;
  completedAt: string | null;
  commentCount: number;
  attachmentCount: number;
}

/**
 * GET /api/projects/{id}/board: the columns and every open task, without pages. canMove is a real
 * member of an open project, canManage an owner or projects.manage of an open one. The API checks both.
 */
export interface BoardDto {
  projectId: string;
  projectKey: string;
  isArchived: boolean;
  canMove: boolean;
  canManage: boolean;
  columns: BoardColumnDto[];
  tasks: BoardTaskDto[];
}

export interface BoardMemberDto {
  userId: string;
  email: string;
  displayName: string;
  role: 'Owner' | 'Member';
  isActive: boolean;
}

/** What POST /api/projects/{id}/tasks answers, as far as the board reads it. */
export interface CreatedTaskDto {
  id: string;
  key: string;
}
