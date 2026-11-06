import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { TaskChanges } from '../../shared/task-changes';
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
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const held = signal<string[]>(['tasks.create']);
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;

  beforeEach(() => {
    board = aBoard();
    held.set(['tasks.create']);
    api = {
      board: vi.fn((): Observable<BoardDto> => of(structuredClone(board))),
      members: vi.fn(() => of([aBoardMember('me', 'Florin Danci'), aBoardMember('u2', 'Ana Pop')])),
      move: vi.fn(() => of({})),
      create: vi.fn(() => of({ id: 'id-KAN-6', key: 'KAN-6' })),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };

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
        {
          provide: AuthStore,
          useValue: { has: (name: string) => held().includes(name), user: () => ({ id: 'me' }) },
        },
        { provide: Snackbar, useValue: snackbar },
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

  it('shows on a card its key, priority, title, deadline and assignee, or an empty avatar', async () => {
    board.tasks[0] = aCard('KAN-1', 'c1', 0, {
      title: 'A',
      priority: 'High',
      dueDate: new Date(Date.now() - 86_400_000).toISOString(),
      assignee: { userId: 'u2', displayName: 'Ana Pop' },
      commentCount: 2,
    });

    await visit();

    const first = card('A');
    expect(first.querySelector('.key')?.textContent.trim()).toBe('KAN-1');
    expect(first.querySelector('.top kb-chip')?.classList).toContain('high');
    expect(first.querySelector('kb-deadline-chip kb-chip')?.classList).toContain(
      'deadline-overdue',
    );
    expect(first.querySelector('kb-avatar')?.getAttribute('aria-label')).toBe('Ana Pop');
    expect(first.querySelector('.count')?.textContent).toContain('2');
    expect(card('B').querySelector('.unassigned')?.getAttribute('aria-label')).toBe('Neasignat');
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
    await visit('/projects/p1/board?assignee=me');

    card('B').click();
    await settle();

    expect(url()).toBe('/projects/p1/board(panel:task/KAN-2)?assignee=me');
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
});
