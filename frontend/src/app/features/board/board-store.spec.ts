import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { BoardApi } from './board-api';
import { BoardStore } from './board-store';
import { aBoard, aBoardMember, refusedWith, titlesByColumn } from './board-testing';
import { BoardColumnDto, BoardDto } from './models';

/**
 * docs/SPEC.md §6.2: the store reads the board through resources, moves a card at once and asks the
 * API after, and puts the card back when the API refuses, reading the board again then.
 */
describe('BoardStore', () => {
  let board: BoardDto;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const held = signal<string[]>(['tasks.create']);
  let store: BoardStore;

  beforeEach(() => {
    board = aBoard();
    held.set(['tasks.create']);
    api = {
      board: vi.fn((): Observable<BoardDto> => of(structuredClone(board))),
      members: vi.fn(() =>
        of([aBoardMember('u1', 'Florin Danci'), aBoardMember('u2', 'Ana Pop', false)]),
      ),
      move: vi.fn(() => of({})),
      create: vi.fn(() => of({ id: 'id-KAN-6', key: 'KAN-6' })),
      addColumn: vi.fn(() => of({ id: 'c4', name: 'Review', position: 3, isDone: false })),
      updateColumn: vi.fn((_: string, columnId: string, name: string, isDone: boolean) =>
        of({ ...board.columns.find((column) => column.id === columnId)!, name, isDone }),
      ),
      moveColumn: vi.fn(() => of([])),
      deleteColumn: vi.fn(() => of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        BoardStore,
        { provide: BoardApi, useValue: api },
        {
          provide: AuthStore,
          useValue: { has: (name: string) => held().includes(name), user: () => ({ id: 'u1' }) },
        },
      ],
    });
    store = TestBed.inject(BoardStore);
  });

  /** The resources load in effects: they run on a tick, and answer on the next turn. */
  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      TestBed.tick();
      await new Promise((resolve) => setTimeout(resolve));
    }
    await TestBed.inject(ApplicationRef).whenStable();
  }

  async function opened(): Promise<void> {
    store.open('p1');
    await settle();
  }

  function shown(): string {
    return titlesByColumn(store.board()!.tasks, store.columns());
  }

  function id(title: string): string {
    return store.board()!.tasks.find((task) => task.title === title)!.id;
  }

  describe('reading', () => {
    it('reads nothing until a project is open', async () => {
      await settle();

      expect(api['board']).not.toHaveBeenCalled();
      expect(store.state()).toBe('loading');
    });

    it('reads the board and the members of the project, the columns from the left', async () => {
      await opened();

      expect(api['board']).toHaveBeenCalledWith('p1');
      expect(store.state()).toBe('ready');
      expect(store.columns().map((column) => column.name)).toEqual([
        'De făcut',
        'În lucru',
        'Gata',
      ]);
      expect(store.cardsIn('c1').map((task) => task.title)).toEqual(['A', 'B', 'C']);
      expect(store.members().map((member) => member.displayName)).toEqual([
        'Florin Danci',
        'Ana Pop',
      ]);
    });

    it('keeps the board on screen while it reads it again', async () => {
      await opened();
      const again = new Subject<BoardDto>();
      api['board'].mockReturnValue(again);

      store.reload();
      TestBed.tick();

      expect(store.state()).toBe('ready');
      expect(shown()).toBe('A B C | D | E');
      again.next({ ...aBoard(), tasks: [] });
      again.complete();
      await settle();
      expect(shown()).toBe(' |  | ');
    });

    it('is in error when the board cannot be read, until it is read again', async () => {
      api['board'].mockReturnValueOnce(refusedWith(500, 'Server'));
      await opened();
      expect(store.state()).toBe('error');
      expect(store.board()).toBeNull();

      store.reload();
      await settle();

      expect(store.state()).toBe('ready');
    });

    it('starts from nothing for another project', async () => {
      await opened();
      const other = new Subject<BoardDto>();
      api['board'].mockReturnValue(other);

      store.open('p2');
      TestBed.tick();

      expect(store.board()).toBeNull();
      expect(store.state()).toBe('loading');
      other.complete();
    });

    // docs/SPEC.md §4.4: moving needs a real membership, creating tasks.create too.
    it('can create only with tasks.create, as one who can move', async () => {
      await opened();
      expect(store.canCreate()).toBe(true);

      held.set([]);
      expect(store.canCreate()).toBe(false);
    });
  });

  describe('moving a card', () => {
    it('moves it at once, before the API answers, and keeps it there once it does', async () => {
      await opened();
      const answer = new Subject<unknown>();
      api['move'].mockReturnValue(answer);

      const moving = store.moveTask(id('B'), 'c3', 0);

      expect(shown()).toBe('A C | D | B E');
      expect(store.board()!.tasks.find((task) => task.title === 'B')!.completedAt).not.toBeNull();
      expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c3', 0);
      answer.next({});
      answer.complete();
      expect(await moving).toBe(true);
      expect(shown()).toBe('A C | D | B E');
      expect(api['board']).toHaveBeenCalledTimes(1);
    });

    it('puts a refused card back where it was, as it was, and reads the board again', async () => {
      api['move'].mockReturnValue(refusedWith(409, 'Task.ChangedMeanwhile'));
      await opened();
      const again = new Subject<BoardDto>();
      api['board'].mockReturnValue(again);

      const moved = await store.moveTask(id('E'), 'c1', 0);

      expect(moved).toBe(false);
      expect(shown()).toBe('A B C | D | E');
      expect(store.board()!.tasks.find((task) => task.title === 'E')!.completedAt).toBe(
        '2026-11-05T08:00:00Z',
      );
      TestBed.tick();
      expect(api['board']).toHaveBeenCalledTimes(2);
      again.complete();
    });

    // Two moves at once: only the refused one goes back, the other stays where it went.
    it('puts back only the refused card when another moved meanwhile', async () => {
      await opened();
      const first = new Subject<unknown>();
      const second = new Subject<unknown>();
      api['move'].mockReturnValueOnce(first).mockReturnValueOnce(second);
      api['board'].mockReturnValue(new Subject<BoardDto>());

      const a = store.moveTask(id('A'), 'c2', 0);
      const c = store.moveTask(id('C'), 'c2', 0);
      expect(shown()).toBe('B | C A D | E');
      first.error(new Error('refused'));
      expect(await a).toBe(false);

      expect(shown()).toBe('A B | C D | E');
      second.next({});
      second.complete();
      expect(await c).toBe(true);
      expect(shown()).toBe('A B | C D | E');
    });

    it('asks nothing for a card let go in its own place', async () => {
      await opened();

      expect(await store.moveTask(id('B'), 'c1', 1)).toBe(true);
      expect(api['move']).not.toHaveBeenCalled();
    });

    it('moves nothing for one who only sees the project', async () => {
      board.canMove = false;
      await opened();

      expect(await store.moveTask(id('A'), 'c2', 0)).toBe(false);
      expect(api['move']).not.toHaveBeenCalled();
      expect(shown()).toBe('A B C | D | E');
    });

    it('moves nothing before the board is read', async () => {
      expect(await store.moveTask('id-KAN-1', 'c2', 0)).toBe(false);
      expect(api['move']).not.toHaveBeenCalled();
    });
  });

  describe('creating a card', () => {
    it('creates it at the end of the column and reads the board again', async () => {
      await opened();

      expect(await store.createTask('c2', 'Login SSO')).toBe('KAN-6');

      expect(api['create']).toHaveBeenCalledWith('p1', 'c2', 'Login SSO');
      TestBed.tick();
      expect(api['board']).toHaveBeenCalledTimes(2);
    });

    it('creates nothing without tasks.create', async () => {
      held.set([]);
      await opened();

      expect(await store.createTask('c2', 'Login SSO')).toBeNull();
      expect(api['create']).not.toHaveBeenCalled();
    });
  });

  describe('changing the columns', () => {
    it('renames a column in place, without reading the board again', async () => {
      await opened();

      expect(await store.renameColumn(store.columns()[1], 'Lucrez')).toBe(true);

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c2', 'Lucrez', false);
      expect(store.columns().map((column) => column.name)).toEqual(['De făcut', 'Lucrez', 'Gata']);
      TestBed.tick();
      expect(api['board']).toHaveBeenCalledTimes(1);
    });

    it('takes the order the API gives after a move', async () => {
      const order: BoardColumnDto[] = [
        { id: 'c3', name: 'Gata', position: 0, isDone: true },
        { id: 'c1', name: 'De făcut', position: 1, isDone: false },
        { id: 'c2', name: 'În lucru', position: 2, isDone: false },
      ];
      api['moveColumn'].mockReturnValue(of(order));
      await opened();

      expect(await store.moveColumn(store.columns()[2], 0)).toBe(true);

      expect(api['moveColumn']).toHaveBeenCalledWith('p1', 'c3', 0);
      expect(store.columns().map((column) => column.name)).toEqual([
        'Gata',
        'De făcut',
        'În lucru',
      ]);
    });

    // docs/SPEC.md §5.1: the tasks of a column turned done are completed, so the board is read again.
    it('reads the board again after a column is marked done, added or deleted', async () => {
      await opened();

      await store.markColumnDone(store.columns()[1], true);
      await store.addColumn('Review');
      await store.deleteColumn(store.columns()[2]);
      await settle();

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c2', 'În lucru', true);
      expect(api['addColumn']).toHaveBeenCalledWith('p1', 'Review', false);
      expect(api['deleteColumn']).toHaveBeenCalledWith('p1', 'c3');
      expect(api['board'].mock.calls.length).toBeGreaterThanOrEqual(2);
    });

    it('reads the board again when the API refuses a change', async () => {
      api['deleteColumn'].mockReturnValue(refusedWith(409, 'Column.NotEmpty'));
      await opened();

      expect(await store.deleteColumn(store.columns()[0])).toBe(false);
      TestBed.tick();

      expect(api['board']).toHaveBeenCalledTimes(2);
    });

    it('changes nothing for one who is not an owner', async () => {
      board.canManage = false;
      await opened();

      expect(await store.addColumn('Review')).toBe(false);
      expect(await store.renameColumn(store.columns()[0], 'X')).toBe(false);
      expect(api['addColumn']).not.toHaveBeenCalled();
      expect(api['updateColumn']).not.toHaveBeenCalled();
    });
  });
});
