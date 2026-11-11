import { LiveAnnouncer } from '@angular/cdk/a11y';
import { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { FakeRealtime, provideFakeRealtime } from '../../core/realtime/realtime-testing';
import { MoveQuestions } from '../../shared/move-questions';
import { TaskChanges } from '../../shared/task-changes';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { BoardApi } from './board-api';
import { BoardColumn } from './board-column';
import { BoardPage } from './board-page';
import { ARCHIVED_COLUMN, ArchivedColumn } from './archived-column';
import { ARCHIVED_STATE_KEY } from './archived-store';
import {
  OWN_COLUMNS,
  aBoard,
  aBoardMember,
  aCard,
  anArchivedCard,
  refusedWith,
} from './board-testing';
import { ArchivedTaskDto, BoardColumnDto, BoardDto, BoardTaskDto } from './models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet /><router-outlet name="panel" />`,
})
class ShellLike {}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
class ProjectLike {}

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class PanelLike {}

describe('BoardPage', () => {
  let board: BoardDto;
  let realtime: FakeRealtime;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const held = signal<string[]>(['tasks.create']);
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let announcer: { announce: ReturnType<typeof vi.fn> };
  let confirmation: { ask: ReturnType<typeof vi.fn>; discardChanges: ReturnType<typeof vi.fn> };
  let questions: { estimate: ReturnType<typeof vi.fn>; pauseReason: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;

  beforeEach(() => {
    board = aBoard();
    held.set(['tasks.create']);
    api = {
      board: vi.fn((): Observable<BoardDto> => of(structuredClone(board))),
      members: vi.fn(() => of([aBoardMember('u1', 'Florin Danci'), aBoardMember('u2', 'Ana Pop')])),
      move: vi.fn(() => of({})),
      create: vi.fn(() => of({ id: 'id-KAN-6', key: 'KAN-6' })),
      addColumn: vi.fn(() => of({ id: 'c4', name: 'Review', position: 3, kind: 'Custom' })),
      updateColumn: vi.fn((_: string, columnId: string, name: string) =>
        of({ ...board.columns.find((column) => column.id === columnId)!, name }),
      ),
      moveColumn: vi.fn(() => of([])),
      deleteColumn: vi.fn(() => of(undefined)),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };
    realtime = new FakeRealtime();
    announcer = { announce: vi.fn(() => Promise.resolve()) };
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    // The sheets of docs/UI_UX_SPEC.md §7.4 answer at once: an hour, or the reason.
    questions = {
      estimate: vi.fn(() => Promise.resolve(60)),
      pauseReason: vi.fn(() => Promise.resolve('Aștept clientul')),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: ShellLike,
            children: [
              {
                path: 'projects/:id',
                component: ProjectLike,
                children: [{ path: 'board', component: BoardPage }],
              },
              { path: 'task/:key', outlet: 'panel', component: PanelLike },
            ],
          },
        ]),
        { provide: BoardApi, useValue: api },
        provideFakeRealtime(realtime),
        {
          provide: AuthStore,
          useValue: { has: (name: string) => held().includes(name), user: () => ({ id: 'u1' }) },
        },
        { provide: Snackbar, useValue: snackbar },
        { provide: LiveAnnouncer, useValue: announcer },
        { provide: Confirmation, useValue: confirmation },
        { provide: MoveQuestions, useValue: questions },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function visit(url = '/projects/p1/board'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create(url);
    await settle();
    return page();
  }

  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      harness.fixture.detectChanges();
      await harness.fixture.whenStable();
    }
  }

  /** As settle, for while a read is held on purpose: whenStable would wait for it. */
  async function settleWhileLoading(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await new Promise((resolve) => setTimeout(resolve));
      harness.fixture.detectChanges();
    }
  }

  /** Another address in the same test: the harness is made once per test. */
  async function go(url: string): Promise<void> {
    await harness.navigateByUrl(url);
    await settle();
  }

  function page(): HTMLElement {
    return harness.routeNativeElement!;
  }

  /** The titles in each column from the left, as the page shows them: "A B C | D | E". */
  function shown(): string {
    return [...page().querySelectorAll('kb-board-column')]
      .map((column) =>
        [...column.querySelectorAll('kb-task-card .title')]
          .map((title) => title.textContent.trim())
          .join(' '),
      )
      .join(' | ');
  }

  function card(title: string): HTMLElement {
    return [...page().querySelectorAll<HTMLElement>('kb-task-card')].find(
      (found) => found.querySelector('.title')?.textContent.trim() === title,
    )!;
  }

  function column(name: string): HTMLElement {
    return [...page().querySelectorAll<HTMLElement>('kb-board-column')].find(
      (found) => found.querySelector('.name')?.textContent.trim() === name,
    )!;
  }

  /** What the CDK says when a card is let go: the card, the list it left and the one it entered. */
  function drop(
    task: BoardTaskDto,
    from: string,
    to: string,
    previousIndex: number,
    currentIndex: number,
  ): void {
    const columns = harness.fixture.debugElement.queryAll(By.directive(BoardColumn));
    const at = (id: string) =>
      columns.find((found) => (found.componentInstance as BoardColumn).column().id === id)!;
    const target = at(to).componentInstance as BoardColumn;
    const source = at(from).componentInstance as BoardColumn;
    target.dropped.emit({
      item: { data: task },
      container: { data: target.column() },
      previousContainer: { data: source.column() },
      previousIndex,
      currentIndex,
    } as unknown as CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>);
  }

  function task(title: string): BoardTaskDto {
    return board.tasks.find((found) => found.title === title)!;
  }

  function url(): string {
    return TestBed.inject(Router).url;
  }

  // axe-core (group 19b): the columns' names are the headings under the page's h1, and the count is said in
  // words, from text a screen reader reads rather than an aria-label on a span.
  it('heads each column with an h2, its count said as words', async () => {
    await visit();

    const todo = column('De făcut');
    expect(todo.querySelector('h2.name')?.textContent.trim()).toBe('De făcut');
    expect(todo.querySelector('h3')).toBeNull();
    const count = todo.querySelector('.count')!;
    expect(count.getAttribute('aria-hidden')).toBe('true');
    expect(count.hasAttribute('aria-label')).toBe(false);
    expect(todo.querySelector('.count + .kb-visually-hidden')?.textContent.trim()).toBe(
      '3 task-uri',
    );
  });

  // docs/UI_UX_SPEC.md §7.4: the columns from the left, each with its cards from the top.
  it('shows the columns from the left, each with its cards in their places and how many they are', async () => {
    board.tasks = [
      aCard('KAN-2', 'c1', 1, { title: 'B' }),
      ...board.tasks.filter((found) => found.title !== 'B'),
    ];

    await visit();

    expect(api['board']).toHaveBeenCalledWith('p1');
    expect(
      [...page().querySelectorAll('kb-board-column .name')].map((name) => name.textContent.trim()),
    ).toEqual(['De făcut', 'În lucru', 'Gata']);
    expect(shown()).toBe('A B C | D | E');
    expect(column('De făcut').querySelector('.count')?.textContent.trim()).toBe('3');
  });

  it('shows on a card its key, priority, title, deadline and assignees, or an empty avatar', async () => {
    board.tasks[0] = aCard('KAN-1', 'c1', 0, {
      title: 'A',
      priority: 'High',
      dueDate: new Date(Date.now() - 86_400_000).toISOString(),
      assignees: [
        { userId: 'u2', displayName: 'Ana Pop' },
        { userId: 'u3', displayName: 'Dan Ionescu' },
        { userId: 'u4', displayName: 'Eva Ene' },
        { userId: 'u5', displayName: 'Ioana Radu' },
      ],
      commentCount: 2,
    });

    await visit();

    const first = card('A');
    expect(first.querySelector('.key')?.textContent.trim()).toBe('KAN-1');
    expect(first.querySelector('.top kb-chip')?.classList).toContain('high');
    expect(first.querySelector('kb-deadline-chip kb-chip')?.classList).toContain(
      'deadline-overdue',
    );
    // docs/UI_UX_SPEC.md §7.4: at most three avatars and +N, every name in the aria-label.
    const stack = first.querySelector('kb-avatar-stack');
    expect(stack?.getAttribute('aria-label')).toBe('Ana Pop, Dan Ionescu, Eva Ene, Ioana Radu');
    expect(stack?.querySelectorAll('kb-avatar').length).toBe(3);
    expect(stack?.querySelector('.more')?.textContent.trim()).toBe('+1');
    expect(first.querySelector('.count')?.textContent).toContain('2');
    expect(card('B').querySelector('.unassigned')?.getAttribute('aria-label')).toBe('Neasignat');
    expect(card('B').querySelector('kb-avatar-stack')).toBeNull();
    expect(card('B').querySelector('.count')).toBeNull();
  });

  // A finished task isn't late: the deadline chip of a card in a done column stays neutral.
  it('leaves the deadline of a card in a done column neutral', async () => {
    board.tasks[4] = {
      ...board.tasks[4],
      dueDate: new Date(Date.now() - 86_400_000).toISOString(),
    };

    await visit();

    expect(card('E').querySelector('kb-deadline-chip kb-chip')?.classList).not.toContain(
      'deadline-overdue',
    );
  });

  it('opens a card in its panel over the board, keeping the query', async () => {
    await visit('/projects/p1/board?priority=Medium');

    card('B').click();
    await settle();

    expect(url()).toBe('/projects/p1/board(panel:task/KAN-2)?priority=Medium');
  });

  // docs/SPEC.md §6.2: the card moves at once, and the API is asked after.
  it('moves a card dropped in another column at once, then asks the API for that place', async () => {
    const answer = new Subject<unknown>();
    api['move'].mockReturnValue(answer);
    await visit();

    drop(task('B'), 'c1', 'c2', 1, 0);
    await settle();

    expect(shown()).toBe('A C | B D | E');
    expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c2', 0, { estimatedMinutes: 60 });
    answer.next({});
    answer.complete();
    await settle();
    expect(shown()).toBe('A C | B D | E');
  });

  it('moves a card within its column', async () => {
    await visit();

    drop(task('A'), 'c1', 'c1', 0, 2);
    await settle();

    expect(shown()).toBe('B C A | D | E');
    expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c1', 2);
  });

  it('asks nothing for a card let go where it was', async () => {
    await visit();

    drop(task('B'), 'c1', 'c1', 1, 1);
    await settle();

    expect(api['move']).not.toHaveBeenCalled();
  });

  // docs/SPEC.md §6.2: refused, the card goes back, and the board is read again.
  it('puts a refused card back where it was at once, then reads the board again', async () => {
    api['move'].mockReturnValue(refusedWith(409, 'Task.ChangedMeanwhile'));
    await visit();
    expect(api['board']).toHaveBeenCalledTimes(1);
    const again = new Subject<BoardDto>();
    api['board'].mockReturnValue(again);

    drop(task('B'), 'c1', 'c3', 1, 1);
    await settleWhileLoading();

    expect(api['board']).toHaveBeenCalledTimes(2);
    expect(shown()).toBe('A B C | D | E');
    expect(card('B').querySelector('kb-deadline-chip')).toBeNull();
    // What the API says now wins: someone else moved D meanwhile.
    const now = aBoard();
    now.tasks[3] = { ...now.tasks[3], columnId: 'c3', position: 1 };
    again.next(now);
    again.complete();
    await settle();
    expect(shown()).toBe('A B C |  | E D');
  });

  it('lets a member without tasks.create move cards but not add them', async () => {
    held.set([]);
    await visit();

    expect(page().querySelector('kb-board-column .add')).toBeNull();
    expect(card('A').classList).not.toContain('cdk-drag-disabled');
  });

  // docs/SPEC.md §4.4: projects.view_all and projects.manage read the board, but only a member changes it.
  it('lets one who only sees the project neither drag a card nor add one', async () => {
    board.canMove = false;
    await visit();

    expect(card('A').classList).toContain('cdk-drag-disabled');
    expect(page().querySelector('kb-board-column .add')).toBeNull();
  });

  it('adds a card at the end of Backlog from its title, and keeps the field for the next one', async () => {
    await visit();
    const backlog = column('De făcut');

    backlog.querySelector<HTMLButtonElement>('.add')!.click();
    await settle();
    const field = backlog.querySelector<HTMLInputElement>('.new-title')!;
    expect(document.activeElement).toBe(field);
    field.value = '  Login SSO ';
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();

    expect(api['create']).toHaveBeenCalledWith('p1', 'c1', 'Login SSO');
    expect(snackbar.show).toHaveBeenCalledWith('Task-ul KAN-6 a fost creat.');
    expect(api['board']).toHaveBeenCalledTimes(2);
    expect(backlog.querySelector<HTMLInputElement>('.new-title')?.value).toBe('');
  });

  it('closes an empty title field with Esc, creating nothing', async () => {
    await visit();
    const todo = column('De făcut');
    todo.querySelector<HTMLButtonElement>('.add')!.click();
    await settle();

    todo
      .querySelector('.new-title')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await settle();

    expect(todo.querySelector('.new-title')).toBeNull();
    expect(api['create']).not.toHaveBeenCalled();
  });

  it('reads the board again when the panel saves a change', async () => {
    await visit();

    TestBed.inject(TaskChanges).saved();
    await settle();

    expect(api['board']).toHaveBeenCalledTimes(2);
  });

  it('says when the board could not be read, and reads it again on Reîncearcă', async () => {
    api['board'].mockReturnValueOnce(refusedWith(500, 'Server'));
    await visit();

    expect(page().textContent).toContain('Nu am putut încărca board-ul.');
    [...page().querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Reîncearcă')!
      .click();
    await settle();

    expect(shown()).toBe('A B C | D | E');
  });

  describe('from the keyboard', () => {
    async function press(title: string, key: string): Promise<void> {
      card(title).dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );
      await settle();
    }

    function said(): string {
      return String(announcer.announce.mock.lastCall?.[0] ?? '');
    }

    // docs/UI_UX_SPEC.md §7.4: Space picks up, the arrows move, Space lets go, Esc gives up.
    it('picks a card up with Space, moves it with the arrows and lets it go with Space', async () => {
      await visit();

      await press('B', ' ');
      expect(card('B').classList).toContain('lifted');
      expect(said()).toContain('«KAN-2»');
      // It keeps its row in the next column, as far as that column goes.
      await press('B', 'ArrowRight');
      expect(shown()).toBe('A C | D B | E');
      expect(said()).toBe('«În lucru», poziția 2 din 2');
      await press('B', 'ArrowUp');
      expect(shown()).toBe('A C | B D | E');
      expect(document.activeElement).toBe(card('B'));
      expect(api['move']).not.toHaveBeenCalled();

      await press('B', ' ');

      expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c2', 0, { estimatedMinutes: 60 });
      expect(shown()).toBe('A C | B D | E');
      expect(card('B').classList).not.toContain('lifted');
      expect(said()).toBe('Mutat în «În lucru», poziția 1 din 2');
    });

    it('moves up within the column and stops at the edges', async () => {
      await visit();

      await press('C', ' ');
      await press('C', 'ArrowUp');
      await press('C', 'ArrowUp');
      await press('C', 'ArrowUp');
      await press('C', 'ArrowLeft');
      expect(shown()).toBe('C A B | D | E');
      expect(said()).toBe('«De făcut», poziția 1 din 3');
      await press('C', ' ');

      expect(api['move']).toHaveBeenCalledWith('id-KAN-3', 'c1', 0);
    });

    it('puts the card back with Esc, asking nothing', async () => {
      await visit();

      await press('A', ' ');
      await press('A', 'ArrowRight');
      await press('A', 'ArrowRight');
      await press('A', 'Escape');

      expect(shown()).toBe('A B C | D | E');
      expect(api['move']).not.toHaveBeenCalled();
      expect(said()).toBe('Mutarea a fost anulată.');
      expect(document.activeElement).toBe(card('A'));
    });

    it('says when the API refused a move made from the keyboard', async () => {
      api['move'].mockReturnValue(refusedWith(409, 'Task.ChangedMeanwhile'));
      await visit();

      await press('A', ' ');
      await press('A', 'ArrowRight');
      await press('A', ' ');

      expect(said()).toBe('Mutarea nu s-a salvat: task-ul a revenit la locul lui.');
    });

    it('picks nothing up for one who only sees the project', async () => {
      board.canMove = false;
      await visit();

      await press('A', ' ');
      await press('A', 'ArrowRight');

      expect(card('A').classList).not.toContain('lifted');
      expect(shown()).toBe('A B C | D | E');
    });

    it('tells a screen reader how to move a card', async () => {
      await visit();

      const help = document.getElementById(card('A').getAttribute('aria-describedby') ?? '');
      expect(help?.textContent).toContain('Space');
    });
  });

  // docs/UI_UX_SPEC.md §7.4: the filters above the board, in the address, as on the List view.
  describe('the filters', () => {
    beforeEach(() => {
      board.tasks[0] = aCard('KAN-1', 'c1', 0, {
        title: 'Login SSO',
        priority: 'High',
        assignees: [{ userId: 'u1', displayName: 'Florin Danci' }],
      });
      board.tasks[3] = aCard('KAN-4', 'c2', 0, {
        title: 'D',
        dueDate: new Date(Date.now() - 86_400_000).toISOString(),
        assignees: [{ userId: 'u2', displayName: 'Ana Pop' }],
      });
      board.tasks[4] = aCard('KAN-5', 'c3', 0, {
        title: 'E',
        dueDate: new Date(Date.now() - 86_400_000).toISOString(),
        completedAt: '2026-11-05T08:00:00Z',
      });
    });

    it('shows only the cards that match the filters in the address', async () => {
      await visit('/projects/p1/board?assignee=me');
      expect(shown()).toBe('Login SSO |  | ');

      await go('/projects/p1/board?assignee=u2');
      expect(shown()).toBe(' | D | ');

      await go('/projects/p1/board?priority=High,Critical');
      expect(shown()).toBe('Login SSO |  | ');

      // Late means open past the deadline, outside a done column.
      await go('/projects/p1/board?overdue=1');
      expect(shown()).toBe(' | D | ');

      await go('/projects/p1/board?q=sso');
      expect(shown()).toBe('Login SSO |  | ');
    });

    // docs/SPEC.md §4.4: a task passes when it has them among its assignees.
    it('finds a card for each of those it is assigned to', async () => {
      board.tasks[1] = aCard('KAN-2', 'c1', 1, {
        title: 'B',
        assignees: [
          { userId: 'u2', displayName: 'Ana Pop' },
          { userId: 'u1', displayName: 'Florin Danci' },
        ],
      });

      await visit('/projects/p1/board?assignee=me');
      expect(shown()).toBe('Login SSO B |  | ');

      await go('/projects/p1/board?assignee=u2');
      expect(shown()).toBe('B | D | ');
    });

    it('counts in a column the cards it shows', async () => {
      await visit('/projects/p1/board?assignee=me');

      expect(column('De făcut').querySelector('.count')?.textContent.trim()).toBe('1');
    });

    it('puts a filter chosen in the bar into the address', async () => {
      await visit();

      [...page().querySelectorAll<HTMLButtonElement>('kb-task-filter-bar button')]
        .find((button) => button.textContent.trim() === 'Întârziate')!
        .click();
      await settle();

      expect(url()).toBe('/projects/p1/board?overdue=1');
      expect(shown()).toBe(' | D | ');
    });

    it('offers no archived tasks on the board', async () => {
      held.set(['tasks.create', 'tasks.archive']);
      await visit();

      expect(page().querySelector('kb-task-filter-bar')?.textContent).not.toContain(
        'Și cele arhivate',
      );
    });

    it('says when nothing matches, and clears the filters', async () => {
      await visit('/projects/p1/board?q=nimic');

      expect(page().textContent).toContain('Niciun task nu se potrivește filtrelor.');
      [...page().querySelectorAll<HTMLButtonElement>('.no-match button')][0].click();
      await settle();

      expect(url()).toBe('/projects/p1/board');
      expect(shown()).toBe('Login SSO B C | D | E');
    });

    // A card dropped among filtered ones lands just before the one it was dropped above: H, hidden, keeps its place.
    it('moves a card dropped among filtered ones to its true place in the column', async () => {
      board.tasks[2] = aCard('KAN-3', 'c1', 2, { title: 'C', priority: 'High' });
      board.tasks[3] = aCard('KAN-4', 'c2', 1, { title: 'D', priority: 'High' });
      board.tasks.push(aCard('KAN-6', 'c2', 0, { title: 'H' }));
      await visit('/projects/p1/board?priority=High');
      expect(shown()).toBe('Login SSO C | D | ');

      drop(task('C'), 'c1', 'c2', 2, 0);
      await settle();

      expect(api['move']).toHaveBeenCalledWith('id-KAN-3', 'c2', 1, { estimatedMinutes: 60 });
    });

    it('moves from the keyboard among the cards it shows', async () => {
      board.tasks[2] = aCard('KAN-3', 'c1', 2, { title: 'C', priority: 'High' });
      await visit('/projects/p1/board?priority=High');

      card('Login SSO').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
      await settle();
      card('Login SSO').dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }),
      );
      await settle();
      card('Login SSO').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
      await settle();

      // After C, the last one shown: B, hidden, keeps its place before it.
      expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c1', 2);
    });
  });

  // docs/UI_UX_SPEC.md §7.4: the ⋮ of a column renames it, moves it, marks it done, deletes it when empty.
  // docs/UI_UX_SPEC.md §7.4 (group 22): Backlog, Analiză (the project's own), În lucru, În pauză, Review (the
  // project's own) and Gata; A in Backlog, B in Analiză, C in În lucru, D in În pauză, E in Gata, Review empty.
  describe('the columns', () => {
    /** The items' icons, whose ligatures the harness reads as words. */
    const ICONS = /^(edit|arrow_back|arrow_forward|delete)\s*/;
    const NO_GOING_BACK = 'Un task început nu se mai întoarce înainte de «În lucru».';

    beforeEach(() => {
      board.columns = OWN_COLUMNS.map((found) => ({ ...found }));
      board.tasks = [
        aCard('KAN-1', 'c1', 0, { title: 'A' }),
        aCard('KAN-2', 'c4', 0, { title: 'B' }),
        aCard('KAN-3', 'c2', 0, { title: 'C' }),
        aCard('KAN-4', 'c5', 0, { title: 'D', pauseReason: 'Aștept clientul' }),
        aCard('KAN-5', 'c3', 0, { title: 'E', completedAt: '2026-11-05T08:00:00Z' }),
      ];
    });

    function menuOf(name: string): HTMLButtonElement | null {
      return column(name).querySelector<HTMLButtonElement>('.menu');
    }

    /** The ⋮ of a column, through its harness: the items' words come without the icons' ligatures. */
    function harnessOf(name: string): Promise<MatMenuHarness> {
      return TestbedHarnessEnvironment.documentRootLoader(harness.fixture).getHarness(
        MatMenuHarness.with({ selector: `[aria-label="Acțiunile coloanei ${name}"]` }),
      );
    }

    async function choose(name: string, item: string): Promise<void> {
      await (await harnessOf(name)).clickItem({ text: new RegExp(`${item}$`) });
      await settle();
    }

    async function items(name: string): Promise<string[]> {
      const menu = await harnessOf(name);
      await menu.open();
      const found = await Promise.all(
        (await menu.getItems()).map(async (entry) => (await entry.getText()).replace(ICONS, '')),
      );
      await menu.close();
      return found;
    }

    function columnOf(id: string): BoardColumn {
      return harness.fixture.debugElement
        .queryAll(By.directive(BoardColumn))
        .map((found) => found.componentInstance as BoardColumn)
        .find((found) => found.column().id === id)!;
    }

    afterEach(() => {
      document.querySelector('.cdk-overlay-container')?.replaceChildren();
    });

    it('marks each fixed column with the bar and the icon of its kind, hidden from screen readers, and none of its own', async () => {
      await visit();

      const kinds = [...page().querySelectorAll('kb-board-column')].map((host) => {
        const kind =
          [...host.querySelector('.column')!.classList].find((name) => name.startsWith('kind-')) ??
          '-';
        const icon = host.querySelector('.kind-icon');
        return `${host.querySelector('.name')!.textContent.trim()} ${kind} ${icon?.textContent.trim() ?? '-'}`;
      });
      expect(kinds).toEqual([
        'Backlog kind-backlog inbox',
        'Analiză kind-custom -',
        'În lucru kind-inprogress play_circle',
        'În pauză kind-paused pause_circle',
        'Review kind-custom -',
        'Gata kind-done check_circle',
      ]);
      expect(
        [...page().querySelectorAll('.kind-icon')].every(
          (icon) => icon.getAttribute('aria-hidden') === 'true' && !icon.hasAttribute('aria-label'),
        ),
      ).toBe(true);
    });

    it('gives an owner a menu on the columns of the project only, and a new column', async () => {
      await visit();

      expect(menuOf('Analiză')?.getAttribute('aria-label')).toBe('Acțiunile coloanei Analiză');
      expect(menuOf('Review')).not.toBeNull();
      for (const fixed of ['Backlog', 'În lucru', 'În pauză', 'Gata']) {
        expect(menuOf(fixed)).toBeNull();
      }
      expect(page().querySelector('.add-column')).not.toBeNull();
    });

    it('gives a member neither the menu nor a new column', async () => {
      board.canManage = false;
      await visit();

      expect(menuOf('Analiză')).toBeNull();
      expect(page().querySelector('.add-column')).toBeNull();
    });

    it('offers "+ Task" in Backlog only', async () => {
      await visit();

      expect(
        [...page().querySelectorAll('kb-board-column')]
          .filter((host) => host.querySelector('.add'))
          .map((host) => host.querySelector('.name')!.textContent.trim()),
      ).toEqual(['Backlog']);
    });

    it('adds a column right before Gata from its name', async () => {
      await visit();

      page().querySelector<HTMLButtonElement>('.add-column')!.click();
      await settle();
      const field = page().querySelector<HTMLInputElement>('.new-column')!;
      expect(document.activeElement).toBe(field);
      field.value = ' QA ';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();

      expect(api['addColumn']).toHaveBeenCalledWith('p1', 'QA');
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(page().querySelector('.new-column')).toBeNull();
    });

    it('renames a column of its own in place: Enter saves, Esc gives up', async () => {
      await visit();

      await choose('Analiză', 'Redenumește');
      let field = column('Analiză').querySelector<HTMLInputElement>('.rename')!;
      expect(document.activeElement).toBe(field);
      field.value = 'Altceva';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await settle();
      expect(api['updateColumn']).not.toHaveBeenCalled();

      await choose('Analiză', 'Redenumește');
      field = column('Analiză').querySelector<HTMLInputElement>('.rename')!;
      field.value = ' Design ';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c4', 'Design');
      expect(column('Design')).toBeDefined();
    });

    // docs/SPEC.md §4.4: Gata is the done column, and no column's kind changes from the menu.
    it('offers no way to mark a column done', async () => {
      await visit();

      expect(await items('Analiză')).not.toContain('Marchează ca terminată');
    });

    it('moves a column of its own only between Backlog and Gata', async () => {
      api['moveColumn'].mockReturnValue(
        of(
          ['c1', 'c4', 'c2', 'c6', 'c5', 'c3'].map((id, position) => ({
            ...OWN_COLUMNS.find((found) => found.id === id)!,
            position,
          })),
        ),
      );
      await visit();
      expect(await items('Analiză')).toEqual(['Redenumește', 'Mută la dreapta']);
      expect(await items('Review')).toEqual(['Redenumește', 'Mută la stânga', 'Șterge']);

      await choose('Review', 'Mută la stânga');

      expect(api['moveColumn']).toHaveBeenCalledWith('p1', 'c6', 3);
      expect(
        [...page().querySelectorAll('kb-board-column .name')].map((name) =>
          name.textContent.trim(),
        ),
      ).toEqual(['Backlog', 'Analiză', 'În lucru', 'Review', 'În pauză', 'Gata']);
    });

    it('offers to delete only an empty column of its own, and asks first', async () => {
      await visit();
      expect(await items('Analiză')).not.toContain('Șterge');

      await choose('Review', 'Șterge');

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Ștergi coloana «Review»?', destructive: true }),
      );
      expect(api['deleteColumn']).toHaveBeenCalledWith('p1', 'c6');
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(snackbar.show).toHaveBeenCalledWith('Coloana «Review» a fost ștearsă.');
    });

    it('deletes nothing when the question is turned down', async () => {
      confirmation.ask.mockReturnValue(Promise.resolve(false));
      await visit();

      await choose('Review', 'Șterge');

      expect(api['deleteColumn']).not.toHaveBeenCalled();
    });

    // docs/UI_UX_SPEC.md §7.4, "Înapoi": a card from În lucru on doesn't enter Backlog, nor Analiză before În lucru.
    it('keeps a started card out of the columns before În lucru while it is dragged', async () => {
      await visit();

      const lists = harness.fixture.debugElement.queryAll(By.directive(CdkDropList));
      const takes = (title: string) =>
        lists.map((found) => {
          const list = found.injector.get(CdkDropList<BoardColumnDto>);
          const drag = { data: task(title) } as CdkDrag<BoardTaskDto>;
          return `${list.data.name} ${list.enterPredicate(drag, list) ? 'yes' : 'no'}`;
        });
      expect(takes('C')).toEqual([
        'Backlog no',
        'Analiză no',
        'În lucru yes',
        'În pauză yes',
        'Review yes',
        'Gata yes',
      ]);
      expect(takes('E')).toContain('Backlog no');
      expect(takes('A').every((line) => line.endsWith('yes'))).toBe(true);
    });

    it('puts a started card let go before În lucru back where it was, saying why', async () => {
      await visit();

      drop(task('C'), 'c2', 'c1', 0, 0);
      await settle();

      expect(api['move']).not.toHaveBeenCalled();
      expect(shown()).toBe('A | B | C | D |  | E');
      expect(snackbar.show).toHaveBeenCalledWith(NO_GOING_BACK);
    });

    // The CDK hands a card a column refused back to its own, with the point it was let go at.
    it('says why when the CDK hands back a started card let go over Analiză', async () => {
      await visit();
      const analysis = column('Analiză');
      Object.defineProperty(document, 'elementFromPoint', {
        configurable: true,
        value: (): Element | null => analysis.querySelector('.cards'),
      });

      try {
        const source = columnOf('c2');
        source.dropped.emit({
          item: { data: task('C') },
          container: { data: source.column() },
          previousContainer: { data: source.column() },
          previousIndex: 0,
          currentIndex: 0,
          isPointerOverContainer: false,
          dropPoint: { x: 40, y: 200 },
        } as unknown as CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>);
        await settle();
      } finally {
        Reflect.deleteProperty(document, 'elementFromPoint');
      }

      expect(api['move']).not.toHaveBeenCalled();
      expect(snackbar.show).toHaveBeenCalledWith(NO_GOING_BACK);
    });

    it('moves a started card between the columns from În lucru on, and an unstarted one before it', async () => {
      await visit();

      drop(task('C'), 'c2', 'c6', 0, 0);
      await settle();
      drop(task('A'), 'c1', 'c4', 0, 1);
      await settle();

      expect(api['move']).toHaveBeenCalledWith('id-KAN-3', 'c6', 0);
      expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c4', 1);
      expect(snackbar.show).not.toHaveBeenCalled();
    });

    it('stops a started card at În lucru from the keyboard', async () => {
      await visit();
      const press = async (title: string, key: string) => {
        card(title).dispatchEvent(
          new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
        );
        await settle();
      };

      await press('C', ' ');
      await press('C', 'ArrowLeft');

      expect(String(announcer.announce.mock.lastCall?.[0])).toBe(NO_GOING_BACK);
      expect(shown()).toBe('A | B | C | D |  | E');
      await press('C', 'Escape');
      expect(api['move']).not.toHaveBeenCalled();
    });
  });

  // docs/UI_UX_SPEC.md §7.4 (group 22): Backlog, Analiză, În lucru, În pauză, Review and Gata; A in Backlog
  // without an estimate, C in În lucru, 2 h 10 min of 4 h, D in În pauză, E in Gata, an hour over its estimate.
  describe('what a move asks first', () => {
    beforeEach(() => {
      board.columns = OWN_COLUMNS.map((found) => ({ ...found }));
      board.tasks = [
        aCard('KAN-1', 'c1', 0, { title: 'A' }),
        aCard('KAN-3', 'c2', 0, {
          title: 'C',
          estimatedMinutes: 240,
          workedSeconds: 7_800,
          isWorking: true,
        }),
        aCard('KAN-4', 'c5', 0, {
          title: 'D',
          estimatedMinutes: 60,
          pauseReason: 'Aștept clientul',
        }),
        aCard('KAN-6', 'c5', 1, { title: 'F' }),
        aCard('KAN-5', 'c3', 0, {
          title: 'E',
          estimatedMinutes: 60,
          workedSeconds: 7_200,
          completedAt: '2026-11-05T08:00:00Z',
        }),
      ];
    });

    async function press(title: string, key: string): Promise<void> {
      card(title).dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );
      await settle();
    }

    it('asks for an estimate before a card without one starts, and sends it with the move', async () => {
      await visit();

      drop(task('A'), 'c1', 'c2', 0, 0);
      await settle();

      expect(questions.estimate).toHaveBeenCalledWith('KAN-1');
      expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c2', 0, { estimatedMinutes: 60 });
      expect(shown()).toBe(' |  | A C | D F |  | E');
      expect(card('A').querySelector('.worked')?.textContent.trim()).toBe('timer 0 min / 1 h');
    });

    it('moves a card that has an estimate into În lucru without asking', async () => {
      board.tasks[0] = aCard('KAN-1', 'c1', 0, { title: 'A', estimatedMinutes: 120 });
      await visit();

      drop(task('A'), 'c1', 'c2', 0, 1);
      await settle();

      expect(questions.estimate).not.toHaveBeenCalled();
      expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c2', 1);
    });

    it('asks why before every pause, and shows the reason on the card', async () => {
      await visit();

      drop(task('C'), 'c2', 'c5', 0, 2);
      await settle();

      expect(questions.pauseReason).toHaveBeenCalledWith('KAN-3');
      expect(api['move']).toHaveBeenCalledWith('id-KAN-3', 'c5', 2, {
        pauseReason: 'Aștept clientul',
      });
      const paused = card('C');
      expect(paused.querySelector('.reason-text')?.textContent.trim()).toBe('Aștept clientul');
      expect(paused.getAttribute('aria-label')).toBe('KAN-3 C. Pe pauză: Aștept clientul');
      expect(
        harness.fixture.debugElement
          .query(By.css('[data-task-id="id-KAN-3"] .reason'))
          .injector.get(MatTooltip).message,
      ).toBe('Aștept clientul');
      expect(paused.querySelector('.timer')?.classList).not.toContain('working');
    });

    it('asks nothing for a card reordered within În pauză', async () => {
      await visit();

      drop(task('F'), 'c5', 'c5', 1, 0);
      await settle();

      expect(questions.pauseReason).not.toHaveBeenCalled();
      expect(api['move']).toHaveBeenCalledWith('id-KAN-6', 'c5', 0);
    });

    it('puts the card back and asks the API nothing when the sheet is turned down', async () => {
      questions.estimate.mockReturnValue(Promise.resolve(null));
      await visit();

      drop(task('A'), 'c1', 'c2', 0, 0);
      await settle();

      expect(api['move']).not.toHaveBeenCalled();
      expect(shown()).toBe('A |  | C | D F |  | E');
    });

    it('asks from the keyboard too, and says when the sheet was turned down', async () => {
      await visit();

      await press('A', ' ');
      await press('A', 'ArrowRight');
      await press('A', 'ArrowRight');
      await press('A', ' ');
      await settle();

      expect(questions.estimate).toHaveBeenCalledWith('KAN-1');
      expect(api['move']).toHaveBeenCalledWith('id-KAN-1', 'c2', 0, { estimatedMinutes: 60 });

      questions.pauseReason.mockReturnValue(Promise.resolve(null));
      await press('C', ' ');
      await press('C', 'ArrowRight');
      await press('C', ' ');
      await settle();

      expect(questions.pauseReason).toHaveBeenCalledWith('KAN-3');
      expect(String(announcer.announce.mock.lastCall?.[0])).toBe('Mutarea a fost anulată.');
      expect(api['move']).toHaveBeenCalledTimes(1);
    });

    // As of the board's last read: the chip doesn't tick.
    it('shows the time worked against the estimate, danger once over, the timer in accent in În lucru', async () => {
      await visit();

      const working = card('C').querySelector('.worked')!;
      expect(working.textContent.trim()).toBe('timer 2 h 10 min / 4 h');
      expect(working.classList).not.toContain('over');
      expect(working.querySelector('.timer')?.classList).toContain('working');
      expect(working.querySelector('.timer')?.getAttribute('aria-hidden')).toBe('true');
      const over = card('E').querySelector('.worked')!;
      expect(over.textContent.trim()).toBe('timer 2 h / 1 h');
      expect(over.classList).toContain('over');
      expect(card('A').querySelector('.worked')).toBeNull();
      expect(card('D').querySelector('.reason-text')?.textContent.trim()).toBe('Aștept clientul');
    });
  });

  // docs/UI_UX_SPEC.md §7.4 (group 22): Arhivate, for tasks.archive, last; KAN-9 back to În lucru, KAN-8 to Gata.
  describe('the column Arhivate', () => {
    let archivedCards: ArchivedTaskDto[];

    beforeEach(() => {
      localStorage.removeItem(ARCHIVED_STATE_KEY);
      board.canSeeArchived = true;
      archivedCards = [
        anArchivedCard('KAN-9', 'c2', 'În lucru', { title: 'Arhivat din lucru' }),
        anArchivedCard('KAN-8', 'c3', 'Gata', { title: 'Arhivat din Gata', priority: 'Low' }),
      ];
      api['archived'] = vi.fn((_: string, page: number) =>
        of({
          items: page === 1 ? archivedCards : [],
          page,
          pageSize: 50,
          totalCount: archivedCards.length,
        }),
      );
      api['archive'] = vi.fn(() => of(undefined));
      api['restore'] = vi.fn(() => of({}));
    });

    afterEach(() => {
      localStorage.removeItem(ARCHIVED_STATE_KEY);
      document.querySelector('.cdk-overlay-container')?.replaceChildren();
    });

    function archivedColumn(): HTMLElement | null {
      return page().querySelector<HTMLElement>('kb-archived-column');
    }

    function archivedComponent(): ArchivedColumn {
      return harness.fixture.debugElement.query(By.directive(ArchivedColumn))
        .componentInstance as ArchivedColumn;
    }

    async function opened(): Promise<HTMLElement> {
      await visit();
      archivedColumn()!.querySelector<HTMLButtonElement>('.band')!.click();
      await settle();
      return archivedColumn()!;
    }

    // What was archived or restored while the connection was away went unheard, as for the board.
    it('reads its cards again after a reconnection', async () => {
      await visit();
      const reads = api['archived'].mock.calls.length;

      realtime.reconnect();
      await settle();

      expect(api['archived'].mock.calls.length).toBe(reads + 1);
    });

    it('comes last, after + Coloană, only for tasks.archive', async () => {
      await visit();

      const board = page().querySelector('.board')!;
      expect(board.lastElementChild?.tagName.toLowerCase()).toBe('kb-archived-column');
      expect(board.lastElementChild?.previousElementSibling?.classList).toContain(
        'add-column-slot',
      );
      expect(api['archived']).toHaveBeenCalledWith('p1', 1);
    });

    it('stays away, and reads nothing, without tasks.archive', async () => {
      board.canSeeArchived = false;
      await visit();

      expect(archivedColumn()).toBeNull();
      expect(api['archived']).not.toHaveBeenCalled();
    });

    it('is closed at first: a band with its icon, its name downwards and its count, which a click opens', async () => {
      await visit();
      const band = archivedColumn()!.querySelector<HTMLButtonElement>('.band')!;

      expect(band.getAttribute('aria-expanded')).toBe('false');
      expect(band.getAttribute('aria-label')).toBe('Arhivate, 2 task-uri. Deschide coloana.');
      expect(band.querySelector('.kind-icon')?.textContent.trim()).toBe('inventory_2');
      expect(band.querySelector('.band-name')?.textContent.trim()).toBe('Arhivate');
      expect(band.querySelector('.count')?.textContent.trim()).toBe('2');
      expect(archivedColumn()!.querySelector('kb-task-card')).toBeNull();

      band.click();
      await settle();

      expect(archivedColumn()!.querySelector('h2')?.textContent.trim()).toBe('Arhivate');
      expect(localStorage.getItem(ARCHIVED_STATE_KEY)).toBe('open');
    });

    it('opens as this browser left it', async () => {
      localStorage.setItem(ARCHIVED_STATE_KEY, 'open');

      await visit();

      expect(archivedColumn()!.classList).toContain('open');
    });

    it('shows its cards faded, with when they were archived in place of the deadline', async () => {
      archivedCards[0] = anArchivedCard('KAN-9', 'c2', 'În lucru', {
        title: 'Arhivat din lucru',
        dueDate: '2026-11-20T21:59:59Z',
        archivedAt: new Date(Date.now() - 3 * 24 * 3_600_000).toISOString(),
      });
      const column = await opened();

      const cards = [...column.querySelectorAll('kb-task-card')];
      expect(cards.map((found) => found.getAttribute('aria-label'))).toEqual([
        'KAN-9 Arhivat din lucru',
        'KAN-8 Arhivat din Gata',
      ]);
      expect(cards[0].classList).toContain('archived');
      expect(cards[0].querySelector('.archived-at')?.textContent.trim()).toBe(
        'arhivat acum 3 zile',
      );
      expect(cards[0].querySelector('kb-deadline-chip')).toBeNull();
    });

    it('reads 50 at a time, with Încarcă mai multe at the end', async () => {
      archivedCards = Array.from({ length: 50 }, (_, index) =>
        anArchivedCard(`KAN-${String(100 + index)}`, 'c1', 'De făcut'),
      );
      api['archived'] = vi.fn((_: string, page: number) =>
        of({
          items: page === 1 ? archivedCards : [anArchivedCard('KAN-99', 'c1', 'De făcut')],
          page,
          pageSize: 50,
          totalCount: 51,
        }),
      );
      const column = await opened();

      column.querySelector<HTMLButtonElement>('.more')!.click();
      await settle();

      expect(api['archived']).toHaveBeenCalledWith('p1', 2);
      expect(column.querySelectorAll('kb-task-card')).toHaveLength(51);
      expect(column.querySelector('.more')).toBeNull();
    });

    it('says when nothing is archived', async () => {
      archivedCards = [];
      const column = await opened();

      expect(column.querySelector('.empty')?.textContent.trim()).toBe('Niciun task arhivat.');
    });

    it('archives a card of the board let go on it, after the question', async () => {
      await visit();

      archivedComponent().dropped.emit({
        item: { data: task('A') },
        container: { data: ARCHIVED_COLUMN },
        previousContainer: { data: board.columns.find((found) => found.id === 'c1') },
        previousIndex: 0,
        currentIndex: 0,
      } as unknown as CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>);
      await settle();

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Arhivezi task-ul «A»?', confirmLabel: 'Arhivează' }),
      );
      expect(api['archive']).toHaveBeenCalledWith('id-KAN-1');
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(api['archived']).toHaveBeenCalledTimes(2);
      expect(snackbar.show).toHaveBeenCalledWith('Task-ul KAN-1 a fost arhivat.');
      expect(api['move']).not.toHaveBeenCalled();
    });

    it('archives nothing when the question is turned down', async () => {
      confirmation.ask.mockReturnValue(Promise.resolve(false));
      await visit();

      archivedComponent().dropped.emit({
        item: { data: task('A') },
        container: { data: ARCHIVED_COLUMN },
        previousContainer: { data: board.columns.find((found) => found.id === 'c1') },
        previousIndex: 0,
        currentIndex: 0,
      } as unknown as CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>);
      await settle();

      expect(api['archive']).not.toHaveBeenCalled();
    });

    it('restores one of its cards let go anywhere on the board into its own column', async () => {
      await opened();
      const target = harness.fixture.debugElement
        .queryAll(By.directive(BoardColumn))
        .map((found) => found.componentInstance as BoardColumn)
        .find((found) => found.column().id === 'c1')!;

      target.dropped.emit({
        item: { data: archivedCards[0] },
        container: { data: target.column() },
        previousContainer: { data: ARCHIVED_COLUMN },
        previousIndex: 0,
        currentIndex: 0,
      } as unknown as CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>);
      await settle();

      expect(api['restore']).toHaveBeenCalledWith('id-KAN-9');
      expect(api['move']).not.toHaveBeenCalled();
      expect(snackbar.show).toHaveBeenCalledWith('«KAN-9» a revenit în «În lucru».');
      expect(api['board']).toHaveBeenCalledTimes(2);
    });

    it('restores a card from its ⋮, from the keyboard', async () => {
      await opened();

      const menu = await TestbedHarnessEnvironment.documentRootLoader(harness.fixture).getHarness(
        MatMenuHarness.with({ selector: '[aria-label="Acțiunile task-ului KAN-8"]' }),
      );
      await menu.clickItem({ text: /Restaurează$/ });
      await settle();

      expect(api['restore']).toHaveBeenCalledWith('id-KAN-8');
      expect(snackbar.show).toHaveBeenCalledWith('«KAN-8» a revenit în «Gata».');
    });

    it('lets every column of the board take one of its cards, Backlog too, and takes the board cards of a member', async () => {
      board.columns = OWN_COLUMNS.map((found) => ({ ...found }));
      board.tasks = [aCard('KAN-3', 'c2', 0, { title: 'C' })];
      await opened();

      const lists = harness.fixture.debugElement
        .queryAll(By.directive(CdkDropList))
        .map((found) => found.injector.get(CdkDropList<BoardColumnDto>));
      const archivedDrag = { data: archivedCards[0] } as unknown as CdkDrag<BoardTaskDto>;
      const boardDrag = { data: task('C') } as CdkDrag<BoardTaskDto>;
      expect(lists.every((list) => list.enterPredicate(archivedDrag, list))).toBe(true);
      const archivedList = lists.find((list) => list.data.id === ARCHIVED_COLUMN.id)!;
      expect(archivedList.enterPredicate(boardDrag, archivedList)).toBe(true);
    });

    it('takes no card of the board from one who may not move cards', async () => {
      board.canMove = false;
      await visit();

      const list = harness.fixture.debugElement
        .queryAll(By.directive(CdkDropList))
        .map((found) => found.injector.get(CdkDropList<BoardColumnDto>))
        .find((found) => found.data.id === ARCHIVED_COLUMN.id)!;
      expect(list.enterPredicate({ data: task('A') } as CdkDrag<BoardTaskDto>, list)).toBe(false);
    });

    // docs/UI_UX_SPEC.md §7.4: the board's filters don't touch it.
    it('keeps all its cards whatever the filters', async () => {
      localStorage.setItem(ARCHIVED_STATE_KEY, 'open');

      await visit('/projects/p1/board?priority=High');

      expect(archivedColumn()!.querySelectorAll('kb-task-card')).toHaveLength(2);
    });
  });

  // docs/SPEC.md §9 and docs/UI_UX_SPEC.md §7.4: the card moves with its animation and pulses once.
  describe('changes of others', () => {
    function message(taskId: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
      return {
        taskId,
        projectId: 'p1',
        byUserId: 'u2',
        originConnectionId: 'another-tab',
        ...extra,
      };
    }

    function columnNamed(id: string): BoardColumn {
      return harness.fixture.debugElement
        .queryAll(By.directive(BoardColumn))
        .map((found) => found.componentInstance as BoardColumn)
        .find((found) => found.column().id === id)!;
    }

    it('moves a card someone else moved to its new place, and pulses it once', async () => {
      await visit();

      realtime.send('TaskMoved', message('id-KAN-1', { columnId: 'c2', position: 1 }));
      await settle();

      expect(shown()).toBe('B C | D A | E');
      expect(card('A').classList).toContain('pulse-a');
      expect(card('B').classList).not.toContain('pulse-a');
      expect(api['board']).toHaveBeenCalledTimes(1);
    });

    it('pulses a card again each time someone changes it, reading the board quietly', async () => {
      await visit();

      realtime.send('TaskUpdated', message('id-KAN-2', { field: 'Title' }));
      await settle();
      expect(card('B').classList).toContain('pulse-a');
      realtime.send('TaskUpdated', message('id-KAN-2', { field: 'Priority' }));
      await settle();

      expect(card('B').classList).toContain('pulse-b');
      expect(card('B').classList).not.toContain('pulse-a');
      expect(api['board']).toHaveBeenCalledTimes(3);
      expect(page().querySelector('kb-skeleton')).toBeNull();
    });

    it('keeps the board still while a card is dragged, and reads it once the card is let go', async () => {
      await visit();
      columnNamed('c1').dragStarted.emit();

      realtime.send('TaskMoved', message('id-KAN-1', { columnId: 'c2', position: 0 }));
      await settle();
      expect(shown()).toBe('A B C | D | E');
      expect(api['board']).toHaveBeenCalledTimes(1);

      columnNamed('c1').dragEnded.emit();
      await settle();

      expect(api['board']).toHaveBeenCalledTimes(2);
    });

    it('keeps the board still while a card is picked up from the keyboard', async () => {
      await visit();
      card('B').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
      await settle();

      realtime.send('TaskMoved', message('id-KAN-1', { columnId: 'c2', position: 0 }));
      await settle();
      expect(shown()).toBe('A B C | D | E');
      expect(api['board']).toHaveBeenCalledTimes(1);

      card('B').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await settle();

      expect(api['board']).toHaveBeenCalledTimes(2);
    });
  });
});
