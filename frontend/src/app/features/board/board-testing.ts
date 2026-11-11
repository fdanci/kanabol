import { HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { BoardColumnDto, BoardDto, BoardMemberDto, BoardTaskDto } from './models';

/** What the specs of the board build their boards from: a plain file, so importing it runs no spec. */
export const COLUMNS: BoardColumnDto[] = [
  { id: 'c3', name: 'Gata', position: 2, kind: 'Done' },
  { id: 'c1', name: 'De făcut', position: 0, kind: 'Backlog' },
  { id: 'c2', name: 'În lucru', position: 1, kind: 'InProgress' },
];

/**
 * The fixed columns with two of the project's own between them (docs/SPEC.md §4.4): Backlog, Analiză, În
 * lucru, În pauză, Review and Gata, out of order, as the API may give them.
 */
export const OWN_COLUMNS: BoardColumnDto[] = [
  { id: 'c3', name: 'Gata', position: 5, kind: 'Done' },
  { id: 'c1', name: 'Backlog', position: 0, kind: 'Backlog' },
  { id: 'c4', name: 'Analiză', position: 1, kind: 'Custom' },
  { id: 'c6', name: 'Review', position: 4, kind: 'Custom' },
  { id: 'c2', name: 'În lucru', position: 2, kind: 'InProgress' },
  { id: 'c5', name: 'În pauză', position: 3, kind: 'Paused' },
];

export function aCard(
  key: string,
  columnId: string,
  position: number,
  extra: Partial<BoardTaskDto> = {},
): BoardTaskDto {
  const number = Number(key.split('-')[1]);
  return {
    id: `id-${key}`,
    key,
    number,
    title: `Titlul ${key}`,
    columnId,
    position,
    priority: 'Medium',
    dueDate: null,
    assignees: [],
    completedAt: null,
    commentCount: 0,
    attachmentCount: 0,
    estimatedMinutes: null,
    workedSeconds: 0,
    isWorking: false,
    pauseReason: null,
    ...extra,
  };
}

/** A, B and C in De făcut, D in În lucru, E in Gata, done. */
export function aBoard(extra: Partial<BoardDto> = {}): BoardDto {
  return {
    projectId: 'p1',
    projectKey: 'KAN',
    isArchived: false,
    canMove: true,
    canManage: true,
    canSeeArchived: false,
    columns: COLUMNS.map((column) => ({ ...column })),
    tasks: [
      aCard('KAN-1', 'c1', 0, { title: 'A' }),
      aCard('KAN-2', 'c1', 1, { title: 'B' }),
      aCard('KAN-3', 'c1', 2, { title: 'C' }),
      aCard('KAN-4', 'c2', 0, { title: 'D' }),
      aCard('KAN-5', 'c3', 0, { title: 'E', completedAt: '2026-11-05T08:00:00Z' }),
    ],
    ...extra,
  };
}

export function aBoardMember(userId: string, displayName: string, isActive = true): BoardMemberDto {
  return { userId, displayName, email: `${userId}@kanabol.test`, role: 'Member', isActive };
}

export function refusedWith(status: number, code: string): Observable<never> {
  return throwError(
    () => new HttpErrorResponse({ status, error: { code }, url: '/api/tasks/id/position' }),
  );
}

/** The titles of each column from the left, as "A B C | D | E". */
export function titlesByColumn(
  tasks: readonly BoardTaskDto[],
  columns: readonly BoardColumnDto[],
): string {
  return [...columns]
    .sort((a, b) => a.position - b.position)
    .map((column) =>
      tasks
        .filter((task) => task.columnId === column.id)
        .sort((a, b) => a.position - b.position)
        .map((task) => task.title)
        .join(' '),
    )
    .join(' | ');
}
