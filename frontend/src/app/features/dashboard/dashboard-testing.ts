import { MySummaryDto, DashboardProjectDto, DashboardTaskDto } from './models';

/** Fixtures for the panel's specs. A plain file, so importing it runs no spec. */
export function aSummary(extra: Partial<MySummaryDto> = {}): MySummaryDto {
  return { openTasks: 7, overdueTasks: 2, completedThisWeek: 3, activeProjects: 4, ...extra };
}

export function aDashboardTask(extra: Partial<DashboardTaskDto> = {}): DashboardTaskDto {
  return {
    id: 't12',
    key: 'KAN-12',
    title: 'Login SSO',
    projectId: 'p1',
    projectKey: 'KAN',
    projectName: 'Kanabol',
    priority: 'High',
    dueDate: '2026-11-04T21:59:59.999Z',
    columnIsDone: false,
    completedAt: null,
    ...extra,
  };
}

export function aDashboardProject(extra: Partial<DashboardProjectDto> = {}): DashboardProjectDto {
  return {
    id: 'p1',
    name: 'Kanabol',
    key: 'KAN',
    owners: [{ userId: 'u1', displayName: 'Ana Pop' }],
    openTaskCount: 9,
    completedTaskCount: 3,
    ...extra,
  };
}
