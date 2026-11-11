import {
  ApplicationRef,
  EnvironmentInjector,
  createEnvironmentInjector,
  signal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { FakeRealtime, provideFakeRealtime } from '../../core/realtime/realtime-testing';
import { BoardApi } from './board-api';
import { BoardStore } from './board-store';
import {
  OWN_COLUMNS,
  aBoard,
  aBoardMember,
  aCard,
  refusedWith,
  titlesByColumn,
} from './board-testing';
import { BoardColumnDto, BoardDto, MoveAnswers } from './models';

/**
 * docs/SPEC.md §6.2: the store reads the board through resources, moves a card at once and asks the
 * API after, and puts the card back when the API refuses, reading the board again then.
 */
describe('BoardStore', () => {
  let board: BoardDto;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const held = signal<string[]>(['tasks.create']);
  let store: BoardStore;
  let realtime: FakeRealtime;

  beforeEach(() => {
    board = aBoard();
    realtime = new FakeRealtime();
    held.set(['tasks.create']);
    api = {
      board: vi.fn((): Observable<BoardDto> => of(structuredClone(board))),
      members: vi.fn(() =>
        of([aBoardMember('u1', 'Florin Danci'), aBoardMember('u2', 'Ana Pop', false)]),
      ),
      move: vi.fn(() => of({})),
      create: vi.fn(() => of({ id: 'id-KAN-6', key: 'KAN-6' })),
      addColumn: vi.fn(() => of({ id: 'c4', name: 'Review', position: 3, kind: 'Custom' })),
      updateColumn: vi.fn((_: string, columnId: string, name: string) =>
        of({ ...board.columns.find((column) => column.id === columnId)!, name }),
      ),
      moveColumn: vi.fn(() => of([])),
      deleteColumn: vi.fn(() => of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [
        BoardStore,
        provideFakeRealtime(realtime),
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

    // docs/UI_UX_SPEC.md §7.4: the card waits in În lucru while the sheet asks for its estimate.
    it('lets the card wait in its new place while the move asks, then sends the answers and shows them', async () => {
      await opened();
      let answer!: (answers: MoveAnswers | null) => void;

      const moving = store.moveTask(
        id('B'),
        'c2',
        0,
        () => new Promise<MoveAnswers | null>((resolve) => (answer = resolve)),
      );
      await Promise.resolve();
      expect(shown()).toBe('A C | B D | E');
      expect(api['move']).not.toHaveBeenCalled();
      answer({ estimatedMinutes: 90 });

      expect(await moving).toBe(true);
      expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c2', 0, { estimatedMinutes: 90 });
      expect(store.board()!.tasks.find((task) => task.title === 'B')).toMatchObject({
        estimatedMinutes: 90,
        isWorking: true,
        pauseReason: null,
      });
    });

    it('puts the card back, asking the API nothing, when the person gives up', async () => {
      await opened();

      const moved = await store.moveTask(id('B'), 'c2', 0, () => Promise.resolve(null));

      expect(moved).toBe(false);
      expect(shown()).toBe('A B C | D | E');
      expect(api['move']).not.toHaveBeenCalled();
      expect(api['board']).toHaveBeenCalledTimes(1);
    });

    it('puts a paused card back with its reason when the person gives up its move', async () => {
      board.columns = structuredClone(OWN_COLUMNS);
      board.tasks.push(aCard('KAN-6', 'c5', 0, { title: 'P', pauseReason: 'Aștept clientul' }));
      await opened();

      const moved = await store.moveTask(id('P'), 'c2', 0, () => Promise.resolve(null));

      expect(moved).toBe(false);
      expect(store.board()!.tasks.find((task) => task.title === 'P')).toMatchObject({
        columnId: 'c5',
        pauseReason: 'Aștept clientul',
        isWorking: false,
      });
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

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c2', 'Lucrez');
      expect(store.columns().map((column) => column.name)).toEqual(['De făcut', 'Lucrez', 'Gata']);
      TestBed.tick();
      expect(api['board']).toHaveBeenCalledTimes(1);
    });

    it('takes the order the API gives after a move', async () => {
      const order: BoardColumnDto[] = [
        { id: 'c3', name: 'Gata', position: 0, kind: 'Done' },
        { id: 'c1', name: 'De făcut', position: 1, kind: 'Backlog' },
        { id: 'c2', name: 'În lucru', position: 2, kind: 'InProgress' },
      ];
      api['moveColumn'].mockReturnValue(of(order));
      await opened();

      expect(await store.moveColumn(store.columns()[2], 0)).toBe('moved');

      expect(api['moveColumn']).toHaveBeenCalledWith('p1', 'c3', 0);
      expect(store.columns().map((column) => column.name)).toEqual([
        'Gata',
        'De făcut',
        'În lucru',
      ]);
    });

    // docs/UI_UX_SPEC.md §7.4 (group 23): a column moved takes its place at once, before the API answers.
    it('moves a column at once, before the API answers, and puts it back when refused', async () => {
      const answer = new Subject<BoardColumnDto[]>();
      api['moveColumn'].mockReturnValue(answer);
      await opened();
      const before = store.columns().map((column) => column.name);

      const moving = store.moveColumn(store.columns()[2], 1);
      TestBed.tick();

      expect(store.columns().map((column) => `${column.name}:${String(column.position)}`)).toEqual([
        `${before[0]}:0`,
        `${before[2]}:1`,
        `${before[1]}:2`,
      ]);
      answer.error(new Error('refused'));
      expect(await moving).toBe('refused');
      expect(store.columns().map((column) => column.name)).toEqual(before);
    });

    // docs/UI_UX_SPEC.md §7.4 (group 23): the page says this one in a dialog of its own.
    it('answers crossesInProgress at a Column.CrossesInProgress of the API, the column back in its place', async () => {
      api['moveColumn'].mockReturnValue(refusedWith(409, 'Column.CrossesInProgress'));
      await opened();
      const before = store.columns().map((column) => column.name);

      expect(await store.moveColumn(store.columns()[2], 1)).toBe('crossesInProgress');
      expect(store.columns().map((column) => column.name)).toEqual(before);
    });

    it('reads the board again after a column is added or deleted', async () => {
      await opened();

      await store.addColumn('Review');
      await store.deleteColumn(store.columns()[2]);
      await settle();

      expect(api['addColumn']).toHaveBeenCalledWith('p1', 'Review');
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
      board.canManageColumns = false;
      await opened();

      expect(await store.addColumn('Review')).toBe(false);
      expect(await store.renameColumn(store.columns()[0], 'X')).toBe(false);
      expect(api['addColumn']).not.toHaveBeenCalled();
      expect(api['updateColumn']).not.toHaveBeenCalled();
    });
  });

  // docs/SPEC.md §4.4, group 23: canManageColumns decides the columns, whatever canManage says.
  describe('columns.manage', () => {
    it('changes the columns for a member with columns.manage', async () => {
      board.canManage = false;
      board.canManageColumns = true;
      await opened();

      expect(await store.addColumn('Review')).toBe(true);
      expect(api['addColumn']).toHaveBeenCalled();
    });

    it('changes nothing for one who manages the project but not its columns', async () => {
      board.canManage = true;
      board.canManageColumns = false;
      await opened();

      expect(await store.addColumn('Review')).toBe(false);
      expect(api['addColumn']).not.toHaveBeenCalled();
    });
  });

  // docs/SPEC.md §9 and docs/UI_UX_SPEC.md §7.4: what others change reaches the board as it happens.
  describe('changes of others', () => {
    /** A message of the hub about task A of p1, from another tab. */
    function about(title: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
      return {
        taskId: id(title),
        projectId: 'p1',
        byUserId: 'u2',
        originConnectionId: 'another-tab',
        ...extra,
      };
    }

    function reads(): number {
      return api['board'].mock.calls.length;
    }

    it('joins the project it opens, and leaves it for the next one', async () => {
      await opened();
      expect(realtime.joined).toEqual(['p1']);

      store.open('p2');

      expect(realtime.left).toEqual(['p1']);
      expect(realtime.joined).toEqual(['p1', 'p2']);
    });

    it('leaves its project and stops listening when its page goes', () => {
      const injector = createEnvironmentInjector([BoardStore], TestBed.inject(EnvironmentInjector));
      const own = injector.get(BoardStore);
      own.open('p1');
      expect(realtime.listening('TaskMoved')).toBe(2);

      injector.destroy();

      expect(realtime.left).toEqual(['p1']);
      expect(realtime.listening('TaskMoved')).toBe(1);
    });

    it('moves a card someone else moved, without reading the board again, and pulses it', async () => {
      await opened();

      realtime.send('TaskMoved', about('A', { columnId: 'c2', position: 0 }));
      await settle();

      expect(shown()).toBe('B C | A D | E');
      expect(store.cardsIn('c1').map((task) => task.position)).toEqual([0, 1]);
      expect(reads()).toBe(1);
      expect(store.pulses().get(id('A'))).toBe(1);
    });

    // The move's message has no reason (docs/SPEC.md §9): a card paused elsewhere reads the board for it, and
    // one that left În pauză forgets the reason it had there.
    it('reads the board again for a card someone else paused, and forgets the reason of one they took out', async () => {
      board.columns = structuredClone(OWN_COLUMNS);
      board.tasks.push(aCard('KAN-6', 'c5', 0, { title: 'P', pauseReason: 'Aștept clientul' }));
      await opened();

      realtime.send('TaskMoved', about('P', { columnId: 'c2', position: 0 }));
      await settle();
      expect(store.board()!.tasks.find((task) => task.title === 'P')).toMatchObject({
        pauseReason: null,
        isWorking: true,
      });
      expect(reads()).toBe(1);

      realtime.send('TaskMoved', about('A', { columnId: 'c5', position: 0 }));
      await settle();
      expect(reads()).toBe(2);
    });

    it('lets the page draw the move: it measures before, and the cards change when it says', async () => {
      await opened();
      let change: (() => void) | undefined;
      store.animateMovesWith((taskId, apply) => {
        expect(taskId).toBe(id('A'));
        change = apply;
      });

      realtime.send('TaskMoved', about('A', { columnId: 'c2', position: 1 }));
      expect(shown()).toBe('A B C | D | E');
      change?.();

      expect(shown()).toBe('B C | D A | E');
    });

    it('ignores what its own tab did, which it shows already', async () => {
      await opened();
      const mine = { originConnectionId: 'my-tab', byUserId: 'u1' };

      realtime.send('TaskMoved', about('A', { columnId: 'c2', position: 0, ...mine }));
      realtime.send('TaskUpdated', about('B', { field: 'Title', ...mine }));
      await settle();

      expect(shown()).toBe('A B C | D | E');
      expect(reads()).toBe(1);
      expect(store.pulses().size).toBe(0);
    });

    // Every event tells the whole project which connection made it, so anyone could send this tab's id
    // in X-Connection-Id: only the signed-in user's own change from this tab is ignored.
    it('hears a change of someone else that claims to come from this tab', async () => {
      await opened();

      realtime.send(
        'TaskMoved',
        about('A', { columnId: 'c2', position: 0, originConnectionId: 'my-tab' }),
      );
      await settle();
      expect(shown()).toBe('B C | A D | E');
      realtime.send('TaskUpdated', about('B', { field: 'Title', originConnectionId: 'my-tab' }));
      await settle();

      expect(reads()).toBe(2);
      expect(store.pulses().get(id('B'))).toBe(1);
    });

    // docs/SPEC.md §9, group 23: a change of the columns elsewhere reads the board again, quietly.
    it('reads itself again quietly on ColumnsChanged from another tab, and not from its own', async () => {
      await opened();

      realtime.send('ColumnsChanged', {
        projectId: 'p1',
        byUserId: 'u2',
        originConnectionId: 'another-tab',
      });
      await settle();
      expect(reads()).toBe(2);
      expect(store.state()).toBe('ready');

      realtime.send('ColumnsChanged', {
        projectId: 'p9',
        byUserId: 'u2',
        originConnectionId: 'another-tab',
      });
      realtime.send('ColumnsChanged', {
        projectId: 'p1',
        byUserId: 'u1',
        originConnectionId: 'my-tab',
      });
      await settle();
      expect(reads()).toBe(2);
    });

    it('ignores the changes of other projects', async () => {
      await opened();

      realtime.send('TaskMoved', about('A', { columnId: 'c2', position: 0, projectId: 'p9' }));
      realtime.send('TaskCreated', about('A', { projectId: 'p9' }));
      await settle();

      expect(shown()).toBe('A B C | D | E');
      expect(reads()).toBe(1);
    });

    it('reads itself again quietly and pulses the card on TaskCreated, TaskUpdated and TaskRestored', async () => {
      await opened();
      const again = new Subject<BoardDto>();
      api['board'].mockReturnValue(again);

      realtime.send('TaskUpdated', about('B', { field: 'Assignees' }));
      TestBed.tick();

      expect(reads()).toBe(2);
      expect(store.state()).toBe('ready');
      expect(shown()).toBe('A B C | D | E');
      expect(store.pulses().get(id('B'))).toBe(1);
      again.next(structuredClone(board));
      again.complete();
      await settle();

      api['board'].mockImplementation(() => of(structuredClone(board)));
      realtime.send('TaskCreated', about('C', { taskId: 'id-KAN-6' }));
      await settle();
      realtime.send('TaskRestored', about('C', { taskId: 'id-KAN-7' }));
      await settle();
      realtime.send('TaskUpdated', about('B', { field: 'Title' }));
      await settle();

      expect(reads()).toBe(5);
      expect(store.pulses().get('id-KAN-6')).toBe(1);
      expect(store.pulses().get('id-KAN-7')).toBe(1);
      expect(store.pulses().get(id('B'))).toBe(2);
    });

    // docs/SPEC.md §9 (group 15): a comment and a file change the counts on the card.
    it('reads itself again quietly and pulses the card on CommentAdded and on TaskUpdated with Attachments', async () => {
      await opened();

      realtime.send('CommentAdded', about('B', { commentId: 'cm1' }));
      await settle();
      realtime.send('TaskUpdated', about('B', { field: 'Attachments' }));
      await settle();

      expect(reads()).toBe(3);
      expect(store.pulses().get(id('B'))).toBe(2);
    });

    it('ignores a comment its own tab wrote', async () => {
      await opened();

      realtime.send(
        'CommentAdded',
        about('B', { commentId: 'cm1', originConnectionId: 'my-tab', byUserId: 'u1' }),
      );
      await settle();

      expect(reads()).toBe(1);
      expect(store.pulses().size).toBe(0);
    });

    it('reads itself again on TaskArchived, the card having nothing left to pulse', async () => {
      await opened();

      realtime.send('TaskArchived', about('B'));
      await settle();

      expect(reads()).toBe(2);
      expect(store.pulses().size).toBe(0);
    });

    it('reads itself again for the move of a card it does not have', async () => {
      await opened();

      realtime.send('TaskMoved', about('A', { taskId: 'id-KAN-9', columnId: 'c2', position: 0 }));
      await settle();

      expect(reads()).toBe(2);
    });

    it('reads itself again when the connection comes back, for what it missed', async () => {
      await opened();

      realtime.reconnect();
      await settle();

      expect(reads()).toBe(2);
    });

    it('waits for its own move to be answered before reading again', async () => {
      await opened();
      const answer = new Subject<unknown>();
      api['move'].mockReturnValue(answer);
      const moving = store.moveTask(id('A'), 'c2', 0);

      realtime.send('TaskUpdated', about('B', { field: 'Title' }));
      realtime.send('TaskMoved', about('C', { columnId: 'c3', position: 0 }));
      await settle();
      expect(reads()).toBe(1);
      expect(shown()).toBe('B C | A D | E');

      answer.next({});
      answer.complete();
      expect(await moving).toBe(true);
      await settle();

      expect(reads()).toBe(2);
    });

    it('waits while a card is in hand, and reads itself once it is let go', async () => {
      await opened();
      store.hold();

      realtime.send('TaskMoved', about('B', { columnId: 'c2', position: 0 }));
      await settle();
      expect(shown()).toBe('A B C | D | E');
      expect(reads()).toBe(1);

      store.release();
      await settle();

      expect(reads()).toBe(2);
    });

    it('reads again a board whose first read was still on its way', async () => {
      const first = new Subject<BoardDto>();
      api['board'].mockReturnValueOnce(first);
      store.open('p1');
      TestBed.tick();

      realtime.send('TaskUpdated', {
        taskId: 'id-KAN-2',
        projectId: 'p1',
        byUserId: 'u2',
        originConnectionId: 'another-tab',
        field: 'Title',
      });
      first.next(structuredClone(board));
      first.complete();
      await settle();

      expect(reads()).toBe(2);
    });
  });
});
