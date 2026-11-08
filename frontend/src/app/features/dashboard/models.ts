import { TaskPriority } from '../../shared/models/task-filters';

/** GET /api/me/summary (docs/SPEC.md §4.4, "Panoul principal și task-urile mele"): the four tiles. */
export interface MySummaryDto {
  /** Assigned to me, not completed. */
  openTasks: number;
  /** Open ones past their deadline. */
  overdueTasks: number;
  /** Completed since the Monday at midnight the panel sent. */
  completedThisWeek: number;
  /** Open projects I'm a member of. */
  activeProjects: number;
}

export interface DashboardPersonDto {
  userId: string;
  displayName: string;
}

/** A row of GET /api/me/tasks, as much of it as the panel's table shows. */
export interface DashboardTaskDto {
  id: string;
  key: string;
  title: string;
  projectId: string;
  projectKey: string;
  projectName: string;
  priority: TaskPriority;
  dueDate: string | null;
  columnIsDone: boolean;
  completedAt: string | null;
}

/** A row of GET /api/projects?mine=true, as much of it as a card of "Proiectele mele" shows. */
export interface DashboardProjectDto {
  id: string;
  name: string;
  key: string;
  owners: DashboardPersonDto[];
  /** Outside a done column, archived tasks left out. */
  openTaskCount: number;
  /** With completed_at, archived tasks left out: the progress is finished / (open + finished). */
  completedTaskCount: number;
}
