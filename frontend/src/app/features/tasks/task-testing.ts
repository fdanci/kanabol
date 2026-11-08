import { HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import {
  AttachmentDto,
  CommentDto,
  TaskHistoryEntryDto,
  TaskItemDto,
  TaskMemberDto,
  TaskProjectDto,
} from './models';

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
    assignees: [],
    columnId: 'c1',
    columnName: 'De făcut',
    position: 0,
    completedAt: null,
    archivedAt: null,
    createdAt: '2026-11-05T08:00:00Z',
    createdBy: { userId: 'me', displayName: 'Florin Danci' },
    updatedAt: null,
    attachments: [],
    canChange: true,
    ...extra,
  };
}

export function aComment(extra: Partial<CommentDto> = {}): CommentDto {
  return {
    id: 'cm1',
    taskId: 't1',
    author: { userId: 'me', displayName: 'Florin Danci' },
    body: 'Merge',
    createdAt: '2026-11-07T08:00:00Z',
    updatedAt: null,
    canEdit: true,
    ...extra,
  };
}

export function anAttachment(extra: Partial<AttachmentDto> = {}): AttachmentDto {
  return {
    id: 'a1',
    fileName: 'brief.pdf',
    contentType: 'application/pdf',
    size: 1_234,
    uploadedBy: { userId: 'me', displayName: 'Florin Danci' },
    uploadedAt: '2026-11-07T08:00:00Z',
    canDelete: true,
    ...extra,
  };
}

/** A change of the title by Florin, unless told otherwise; names only for the ids its values hold. */
export function anEntry(extra: Partial<TaskHistoryEntryDto> = {}): TaskHistoryEntryDto {
  return {
    id: 'h1',
    field: 'Title',
    oldValue: null,
    newValue: null,
    changedAt: '2026-11-08T10:00:00Z',
    changedBy: { userId: 'me', displayName: 'Florin Danci' },
    names: {},
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
