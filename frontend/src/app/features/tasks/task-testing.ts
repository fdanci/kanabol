import { HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { TaskItemDto, TaskMemberDto, TaskProjectDto } from './models';

/** What the specs of the tasks feature build their tasks, projects and refusals from. */
export function aTask(extra: Partial<TaskItemDto> = {}): TaskItemDto {
  return {
    id: 't1',
    projectId: 'p1',
    projectName: 'Kanabol',
    key: 'KAN-1',
    number: 1,
    title: 'Login SSO',
    description: null,
    priority: 'Medium',
    dueDate: null,
    assignee: null,
    columnId: 'c1',
    columnName: 'De făcut',
    position: 0,
    completedAt: null,
    archivedAt: null,
    createdAt: '2026-11-05T08:00:00Z',
    createdBy: { userId: 'me', displayName: 'Florin Danci' },
    updatedAt: null,
    canChange: true,
    ...extra,
  };
}

export function aProject(): TaskProjectDto {
  return {
    id: 'p1',
    name: 'Kanabol',
    key: 'KAN',
    archivedAt: null,
    myRole: 'Owner',
    canManage: true,
    columns: [
      { id: 'c3', name: 'Gata', position: 2, isDone: true },
      { id: 'c1', name: 'De făcut', position: 0, isDone: false },
      { id: 'c2', name: 'În lucru', position: 1, isDone: false },
    ],
  };
}

export function aMember(userId: string, displayName: string, isActive = true): TaskMemberDto {
  return { userId, displayName, email: `${userId}@kanabol.test`, role: 'Member', isActive };
}

export function refused(
  status: number,
  code: string,
  errors?: Record<string, string[]>,
): Observable<never> {
  return throwError(
    () => new HttpErrorResponse({ status, error: { code, errors }, url: '/api/tasks' }),
  );
}
