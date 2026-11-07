import { LiveAnnouncer } from '@angular/cdk/a11y';
import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { FakeRealtime, provideFakeRealtime } from '../../core/realtime/realtime-testing';
import { TaskChanges } from '../../shared/task-changes';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { BoardApi } from './board-api';
import { BoardColumn } from './board-column';
import { BoardPage } from './board-page';
import { aBoard, aBoardMember, aCard, refusedWith } from './board-testing';
import { BoardColumnDto, BoardDto, BoardTaskDto } from './models';

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
  let harness: RouterTestingHarness;

  beforeEach(() => {
    board = aBoard();
    held.set(['tasks.create']);
    api = {
      board: vi.fn((): Observable<BoardDto> => of(structuredClone(board))),
      members: vi.fn(() => of([aBoardMember('u1', 'Florin Danci'), aBoardMember('u2', 'Ana Pop')])),
      move: vi.fn(() => of({})),
      create: vi.fn(() => of({ id: 'id-KAN-6', key: 'KAN-6' })),
      addColumn: vi.fn(() => of({ id: 'c4', name: 'Review', position: 3, isDone: false })),
      updateColumn: vi.fn((_: string, columnId: string, name: string, isDone: boolean) =>
        of({ ...board.columns.find((column) => column.id === columnId)!, name, isDone }),
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
    expect(column('Gata').querySelector('.done')).not.toBeNull();
    expect(column('De făcut').querySelector('.done')).toBeNull();
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
    expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c2', 0);
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

  it('adds a card at the end of a column from its title, and keeps the field for the next one', async () => {
    await visit();
    const inProgress = column('În lucru');

    inProgress.querySelector<HTMLButtonElement>('.add')!.click();
    await settle();
    const field = inProgress.querySelector<HTMLInputElement>('.new-title')!;
    expect(document.activeElement).toBe(field);
    field.value = '  Login SSO ';
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();

    expect(api['create']).toHaveBeenCalledWith('p1', 'c2', 'Login SSO');
    expect(snackbar.show).toHaveBeenCalledWith('Task-ul KAN-6 a fost creat.');
    expect(api['board']).toHaveBeenCalledTimes(2);
    expect(inProgress.querySelector<HTMLInputElement>('.new-title')?.value).toBe('');
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

      expect(api['move']).toHaveBeenCalledWith('id-KAN-2', 'c2', 0);
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

    // A card dropped among filtered ones lands just before the one it was dropped above.
    it('moves a card dropped among filtered ones to its true place in the column', async () => {
      board.tasks[2] = aCard('KAN-3', 'c1', 2, { title: 'C', priority: 'High' });
      board.tasks[3] = aCard('KAN-4', 'c2', 0, { title: 'D', priority: 'High' });
      await visit('/projects/p1/board?priority=High');
      expect(shown()).toBe('Login SSO C | D | ');

      drop(task('D'), 'c2', 'c1', 0, 1);
      await settle();

      expect(api['move']).toHaveBeenCalledWith('id-KAN-4', 'c1', 2);
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
  describe('the columns', () => {
    /** The items' icons, whose ligatures the harness reads as words. */
    const ICONS = /^(edit|arrow_back|arrow_forward|remove_done|check_circle|delete)\s*/;

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

    afterEach(() => {
      document.querySelector('.cdk-overlay-container')?.replaceChildren();
    });

    it('gives an owner a menu on each column', async () => {
      await visit();

      expect(menuOf('De făcut')?.getAttribute('aria-label')).toBe('Acțiunile coloanei De făcut');
      expect(page().querySelector('.add-column')).not.toBeNull();
    });

    it('gives a member neither the menu nor a new column', async () => {
      board.canManage = false;
      await visit();

      expect(menuOf('De făcut')).toBeNull();
      expect(page().querySelector('.add-column')).toBeNull();
    });

    it('adds a column at the right end from its name', async () => {
      await visit();

      page().querySelector<HTMLButtonElement>('.add-column')!.click();
      await settle();
      const field = page().querySelector<HTMLInputElement>('.new-column')!;
      expect(document.activeElement).toBe(field);
      field.value = ' Review ';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();

      expect(api['addColumn']).toHaveBeenCalledWith('p1', 'Review', false);
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(page().querySelector('.new-column')).toBeNull();
    });

    it('renames a column in place: Enter saves, Esc gives up', async () => {
      await visit();

      await choose('În lucru', 'Redenumește');
      let field = column('În lucru').querySelector<HTMLInputElement>('.rename')!;
      expect(document.activeElement).toBe(field);
      field.value = 'Altceva';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await settle();
      expect(api['updateColumn']).not.toHaveBeenCalled();

      await choose('În lucru', 'Redenumește');
      field = column('În lucru').querySelector<HTMLInputElement>('.rename')!;
      field.value = ' Lucrez ';
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c2', 'Lucrez', false);
      expect(column('Lucrez')).toBeDefined();
    });

    // docs/SPEC.md §5.1: the tasks in a column turned done are completed, so the board is read again.
    it('marks a column done, and back, reading the board again each time', async () => {
      await visit();

      await choose('În lucru', 'Marchează ca terminată');

      expect(api['updateColumn']).toHaveBeenCalledWith('p1', 'c2', 'În lucru', true);
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(snackbar.show).toHaveBeenCalledWith(
        '«În lucru» e acum o coloană de task-uri terminate.',
      );
      expect(await items('Gata')).toContain('Nu mai e de task-uri terminate');
    });

    it('moves a column to the left or the right, and not past the ends', async () => {
      api['moveColumn'].mockReturnValue(
        of([
          { id: 'c1', name: 'De făcut', position: 0, isDone: false },
          { id: 'c3', name: 'Gata', position: 1, isDone: true },
          { id: 'c2', name: 'În lucru', position: 2, isDone: false },
        ]),
      );
      await visit();
      expect(await items('De făcut')).not.toContain('Mută la stânga');
      expect(await items('Gata')).not.toContain('Mută la dreapta');

      await choose('Gata', 'Mută la stânga');

      expect(api['moveColumn']).toHaveBeenCalledWith('p1', 'c3', 1);
      expect(
        [...page().querySelectorAll('kb-board-column .name')].map((name) =>
          name.textContent.trim(),
        ),
      ).toEqual(['De făcut', 'Gata', 'În lucru']);
    });

    it('offers to delete only an empty column, and asks first', async () => {
      board.tasks = board.tasks.filter((task) => task.columnId !== 'c3');
      await visit();
      expect(await items('De făcut')).not.toContain('Șterge');

      await choose('Gata', 'Șterge');

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Ștergi coloana «Gata»?', destructive: true }),
      );
      expect(api['deleteColumn']).toHaveBeenCalledWith('p1', 'c3');
      expect(api['board']).toHaveBeenCalledTimes(2);
      expect(snackbar.show).toHaveBeenCalledWith('Coloana «Gata» a fost ștearsă.');
    });

    it('deletes nothing when the question is turned down', async () => {
      board.tasks = board.tasks.filter((task) => task.columnId !== 'c3');
      confirmation.ask.mockReturnValue(Promise.resolve(false));
      await visit();

      await choose('Gata', 'Șterge');

      expect(api['deleteColumn']).not.toHaveBeenCalled();
    });

    it('never offers to delete the last column', async () => {
      board.columns = [{ id: 'c1', name: 'De făcut', position: 0, isDone: false }];
      board.tasks = [];
      await visit();

      expect(await items('De făcut')).not.toContain('Șterge');
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
