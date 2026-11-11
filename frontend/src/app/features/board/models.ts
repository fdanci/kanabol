import { ColumnKind } from '../../shared/models/column-kind';
import { TaskPriority } from '../../shared/models/task-filters';

export type { ColumnKind } from '../../shared/models/column-kind';
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
  /** The four fixed kinds and the project's own; a task in Done, Gata, is completed (docs/SPEC.md §4.4). */
  kind: ColumnKind;
}

/** A card: what docs/UI_UX_SPEC.md §7.4 puts on it, and its place. Assignees come by name, none for nobody. */
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
  assignees: BoardPersonDto[];
  completedAt: string | null;
  commentCount: number;
  attachmentCount: number;
  /** The task's estimate, worked time up to now, whether it's working and the pause's reason (docs/SPEC.md §4.4). */
  estimatedMinutes: number | null;
  workedSeconds: number;
  isWorking: boolean;
  pauseReason: string | null;
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
  /** tasks.archive in the token: the board shows the column Arhivate (docs/SPEC.md §4.4). */
  canSeeArchived: boolean;
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

/**
 * What a move into În lucru or În pauză asks for first (docs/SPEC.md §4.4): the estimate, in minutes, of a
 * task that starts without one, and the reason of every pause.
 */
export interface MoveAnswers {
  estimatedMinutes?: number;
  pauseReason?: string;
}

/** What POST /api/projects/{id}/tasks answers, as far as the board reads it. */
export interface CreatedTaskDto {
  id: string;
  key: string;
}
