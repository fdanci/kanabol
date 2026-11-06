import { Injectable, computed, inject, resource, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { BoardApi } from './board-api';
import { cardsIn, moveCard } from './board-moves';
import { BoardColumnDto, BoardDto, BoardMemberDto, BoardTaskDto } from './models';

export type BoardState = 'loading' | 'ready' | 'error';

/**
 * The board of the project on screen (docs/SPEC.md §6.2, §6.3). The reads are resources of the
 * project's id; a move changes the cards at once and asks the API after, putting the card back
 * where it was when the API refuses, and reading the board again then, since someone else may have
 * moved things. errorInterceptor has said why by then. One per board page, which provides it.
 */
@Injectable()
export class BoardStore {
  private readonly api = inject(BoardApi);
  private readonly auth = inject(AuthStore);
  private readonly projectId = signal<string | undefined>(undefined);

  private readonly loaded = resource({
    params: () => this.projectId(),
    loader: ({ params }) => firstValueFrom(this.api.board(params)),
  });
  private readonly people = resource({
    params: () => this.projectId(),
    loader: ({ params }) => firstValueFrom(this.api.members(params)),
  });

  /** Kept while the board is read again, so the cards don't blink. */
  readonly board = computed<BoardDto | null>(() =>
    this.loaded.hasValue() ? this.loaded.value() : null,
  );
  readonly state = computed<BoardState>(() => {
    if (this.board()) {
      return 'ready';
    }
    return this.loaded.status() === 'error' ? 'error' : 'loading';
  });
  readonly columns = computed<BoardColumnDto[]>(() =>
    [...(this.board()?.columns ?? [])].sort((a, b) => a.position - b.position),
  );
  /** The cards of each column, from the top. */
  readonly cardsByColumn = computed(() => {
    const tasks = this.board()?.tasks ?? [];
    return new Map(this.columns().map((column) => [column.id, cardsIn(tasks, column.id)]));
  });
  readonly canMove = computed(() => this.board()?.canMove ?? false);
  readonly canManage = computed(() => this.board()?.canManage ?? false);
  /** tasks.create from the token, and a real membership of an open project (docs/SPEC.md §4.4). */
  readonly canCreate = computed(() => this.canMove() && this.auth.has(PERMISSIONS.tasksCreate));
  readonly members = computed<BoardMemberDto[]>(() =>
    this.people.hasValue() ? this.people.value() : [],
  );

  open(projectId: string): void {
    this.projectId.set(projectId);
  }

  reload(): void {
    this.loaded.reload();
  }

  cardsIn(columnId: string): BoardTaskDto[] {
    return this.cardsByColumn().get(columnId) ?? [];
  }

  /** True once the API took the move; false when it refused it, the card being back in its place. */
  async moveTask(taskId: string, columnId: string, position: number): Promise<boolean> {
    const before = this.board();
    const moving = before?.tasks.find((task) => task.id === taskId);
    if (!before || !moving || !before.canMove) {
      return false;
    }
    if (moving.columnId === columnId && moving.position === position) {
      return true;
    }

    this.change((board) =>
      moveCard(board.tasks, board.columns, taskId, columnId, position, new Date()),
    );
    try {
      await firstValueFrom(this.api.move(taskId, columnId, position));
      return true;
    } catch {
      // Back to where it was, among whatever else moved meanwhile, then the board as the API has it.
      this.change((board) =>
        board.tasks.find((task) => task.id === taskId)
          ? moveCard(
              board.tasks,
              board.columns,
              taskId,
              moving.columnId,
              moving.position,
              new Date(),
            ).map((task) =>
              task.id === taskId ? { ...task, completedAt: moving.completedAt } : task,
            )
          : board.tasks,
      );
      this.reload();
      return false;
    }
  }

  /** At the end of the column; the board is read again, so the card comes as the API made it. */
  async createTask(columnId: string, title: string): Promise<string | null> {
    const board = this.board();
    if (!board || !this.canCreate()) {
      return null;
    }
    try {
      const created = await firstValueFrom(this.api.create(board.projectId, columnId, title));
      this.reload();
      return created.key;
    } catch {
      return null;
    }
  }

  private change(tasks: (board: BoardDto) => BoardTaskDto[]): void {
    const board = this.board();
    if (board) {
      this.loaded.value.set({ ...board, tasks: tasks(board) });
    }
  }
}
