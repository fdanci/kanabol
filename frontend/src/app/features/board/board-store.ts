import {
  DestroyRef,
  Injectable,
  computed,
  effect,
  inject,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { TaskMessage, TaskMovedMessage } from '../../core/realtime/models';
import { Realtime } from '../../core/realtime/realtime';
import { BoardApi } from './board-api';
import { cardsIn, moveCard } from './board-moves';
import { BoardColumnDto, BoardDto, BoardMemberDto, BoardTaskDto, MoveAnswers } from './models';

export type BoardState = 'loading' | 'ready' | 'error';

/**
 * The board of the project on screen (docs/SPEC.md §6.2, §6.3). The reads are resources of the
 * project's id; a move changes the cards at once and asks the API after, putting the card back
 * where it was when the API refuses, and reading the board again then, since someone else may have
 * moved things. errorInterceptor has said why by then. One per board page, which provides it.
 *
 * What others change comes from the hub (docs/SPEC.md §9): a move of theirs moves the card here, which
 * the page animates, and the rest makes the board read itself again, quietly; either way the card pulses
 * once (docs/UI_UX_SPEC.md §7.4). What this tab did comes back with its own connection id, and is ignored.
 * While a card is in hand, a move of this tab's waits for the API, or the first read is on its way, what
 * others did waits too, and the board reads itself once after, so nothing moves under the cursor and no
 * read from before a move puts a card back.
 */
@Injectable()
export class BoardStore {
  private readonly api = inject(BoardApi);
  private readonly auth = inject(AuthStore);
  private readonly realtime = inject(Realtime);
  private readonly projectId = signal<string | undefined>(undefined);
  private readonly pulsed = signal<ReadonlyMap<string, number>>(new Map());
  private held = false;
  private moving = 0;
  private stale = false;
  private animate: (taskId: string, change: () => void) => void = (_, change) => {
    change();
  };

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
  /** How many times others changed each card, so it pulses once for each (docs/UI_UX_SPEC.md §7.4). */
  readonly pulses = this.pulsed.asReadonly();

  constructor() {
    const stopListening = [
      this.realtime.on('TaskMoved', (message) => {
        this.heardMove(message);
      }),
      this.realtime.on('TaskCreated', (message) => {
        this.heardChange(message, true);
      }),
      this.realtime.on('TaskUpdated', (message) => {
        this.heardChange(message, true);
      }),
      this.realtime.on('TaskRestored', (message) => {
        this.heardChange(message, true);
      }),
      this.realtime.on('TaskArchived', (message) => {
        this.heardChange(message, false);
      }),
      this.realtime.on('CommentAdded', (message) => {
        this.heardChange(message, true);
      }),
      // Whatever happened while the connection was away went unheard.
      this.realtime.onReconnected(() => {
        this.readAgain();
      }),
    ];
    // A read that answered: what was heard while it was on its way gets read now.
    effect(() => {
      if (this.loaded.status() === 'resolved') {
        untracked(() => {
          this.settle();
        });
      }
    });
    inject(DestroyRef).onDestroy(() => {
      for (const stop of stopListening) {
        stop();
      }
      const projectId = untracked(this.projectId);
      if (projectId !== undefined) {
        void this.realtime.leave(projectId);
      }
    });
  }

  open(projectId: string): void {
    const before = untracked(this.projectId);
    if (before === projectId) {
      return;
    }
    if (before !== undefined) {
      void this.realtime.leave(before);
    }
    this.projectId.set(projectId);
    void this.realtime.join(projectId);
  }

  reload(): void {
    this.readAgain();
  }

  /** The page draws a move of others: it measures the card, lets the change happen, then animates it. */
  animateMovesWith(animate: (taskId: string, change: () => void) => void): void {
    this.animate = animate;
  }

  /** A card picked up, by the mouse or the keyboard: the cards stay still under it. */
  hold(): void {
    this.held = true;
  }

  /** Let go. A drop asks for its move in the same turn, so the board reads itself only after that. */
  release(): void {
    queueMicrotask(() => {
      this.held = false;
      this.settle();
    });
  }

  cardsIn(columnId: string): BoardTaskDto[] {
    return this.cardsByColumn().get(columnId) ?? [];
  }

  /**
   * True once the API took the move; false when it refused it, or the person gave up what the move asked
   * for, the card being back in its place. The card waits in its new place while ask waits for an answer
   * (docs/UI_UX_SPEC.md §7.4), the board keeping still meanwhile, then shows what the answer gave it.
   */
  async moveTask(
    taskId: string,
    columnId: string,
    position: number,
    ask?: () => Promise<MoveAnswers | null>,
  ): Promise<boolean> {
    const before = this.board();
    const moving = before?.tasks.find((task) => task.id === taskId);
    if (!before || !moving || !before.canMove) {
      return false;
    }
    if (moving.columnId === columnId && moving.position === position) {
      return true;
    }

    this.moving++;
    // A read on its way may answer from before this move: the board reads itself once more after it.
    if (untracked(this.loaded.isLoading)) {
      this.stale = true;
    }
    this.change((board) =>
      moveCard(board.tasks, board.columns, taskId, columnId, position, new Date()),
    );
    try {
      const answers = ask ? await ask() : undefined;
      if (answers === null) {
        this.change((board) =>
          board.tasks.find((task) => task.id === taskId)
            ? moveCard(
                board.tasks,
                board.columns,
                taskId,
                moving.columnId,
                moving.position,
                new Date(),
              )
            : board.tasks,
        );
        return false;
      }
      if (answers) {
        this.change((board) => this.withAnswers(board, taskId, columnId, answers));
      }
      await firstValueFrom(
        answers
          ? this.api.move(taskId, columnId, position, answers)
          : this.api.move(taskId, columnId, position),
      );
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
    } finally {
      this.moving--;
      this.settle();
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

  /**
   * The columns (docs/UI_UX_SPEC.md §7.4), for an owner or projects.manage. Each answers true once the
   * API took it; refused, errorInterceptor says why, and the board is read again to be sure.
   */
  addColumn(name: string): Promise<boolean> {
    return this.changeColumns((board) => this.api.addColumn(board.projectId, name), true);
  }

  renameColumn(column: BoardColumnDto, name: string): Promise<boolean> {
    return this.changeColumns(
      (board) => this.api.updateColumn(board.projectId, column.id, name),
      false,
      (saved) => {
        this.placeColumns((columns) =>
          columns.map((found) => (found.id === saved.id ? saved : found)),
        );
      },
    );
  }

  moveColumn(column: BoardColumnDto, position: number): Promise<boolean> {
    return this.changeColumns(
      (board) => this.api.moveColumn(board.projectId, column.id, position),
      false,
      (columns) => {
        this.placeColumns(() => columns);
      },
    );
  }

  deleteColumn(column: BoardColumnDto): Promise<boolean> {
    return this.changeColumns((board) => this.api.deleteColumn(board.projectId, column.id), true);
  }

  private async changeColumns<T>(
    call: (board: BoardDto) => Observable<T>,
    readAgain: boolean,
    apply?: (saved: T) => void,
  ): Promise<boolean> {
    const board = this.board();
    if (!board?.canManage) {
      return false;
    }
    try {
      const saved = await firstValueFrom(call(board), { defaultValue: undefined as T });
      apply?.(saved);
      if (readAgain) {
        this.reload();
      }
      return true;
    } catch {
      this.reload();
      return false;
    }
  }

  private placeColumns(columns: (current: BoardColumnDto[]) => BoardColumnDto[]): void {
    const board = this.board();
    if (board) {
      this.loaded.value.set({ ...board, columns: columns(board.columns) });
    }
  }

  /** Now, or once nothing is in the way: a card in hand, a move of this tab's, the first read. */
  private readAgain(): void {
    const status = untracked(this.loaded.status);
    if (status === 'idle') {
      return;
    }
    if (this.held || this.moving > 0 || status === 'loading') {
      this.stale = true;
      return;
    }
    this.stale = false;
    this.loaded.reload();
  }

  private settle(): void {
    if (this.stale) {
      this.readAgain();
    }
  }

  /**
   * Of this project, and not what this tab did, which it shows already. Every event tells the project
   * which connection made it, so anyone could send this tab's id: only the signed-in user's own change
   * from this tab is ignored, byUserId coming from the API's token, not from a header.
   */
  private concerns(message: TaskMessage): boolean {
    const fromThisTab =
      message.originConnectionId !== null &&
      message.originConnectionId === this.realtime.connectionId() &&
      message.byUserId === this.auth.user()?.id;
    return message.projectId === untracked(this.projectId) && !fromThisTab;
  }

  private heardMove(message: TaskMovedMessage): void {
    if (!this.concerns(message)) {
      return;
    }
    this.pulse(message.taskId);
    const known = this.board()?.tasks.some((task) => task.id === message.taskId) ?? false;
    if (!known || this.held || this.moving > 0 || untracked(this.loaded.isLoading)) {
      this.readAgain();
      return;
    }
    this.animate(message.taskId, () => {
      this.change((board) =>
        moveCard(
          board.tasks,
          board.columns,
          message.taskId,
          message.columnId,
          message.position,
          new Date(),
        ),
      );
    });
  }

  private heardChange(message: TaskMessage, pulses: boolean): void {
    if (!this.concerns(message)) {
      return;
    }
    if (pulses) {
      this.pulse(message.taskId);
    }
    this.readAgain();
  }

  private pulse(taskId: string): void {
    this.pulsed.update((pulses) => new Map(pulses).set(taskId, (pulses.get(taskId) ?? 0) + 1));
  }

  /** The moved card as the API now has it: its estimate, whether it works, and its pause's reason. */
  private withAnswers(
    board: BoardDto,
    taskId: string,
    columnId: string,
    answers: MoveAnswers,
  ): BoardTaskDto[] {
    const kind = board.columns.find((column) => column.id === columnId)?.kind;
    return board.tasks.map((task) =>
      task.id === taskId
        ? {
            ...task,
            estimatedMinutes: answers.estimatedMinutes ?? task.estimatedMinutes,
            isWorking: kind === 'InProgress',
            pauseReason: kind === 'Paused' ? (answers.pauseReason ?? task.pauseReason) : null,
          }
        : task,
    );
  }

  private change(tasks: (board: BoardDto) => BoardTaskDto[]): void {
    const board = this.board();
    if (board) {
      this.loaded.value.set({ ...board, tasks: tasks(board) });
    }
  }
}
