import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HarnessLoader, TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Page } from '../../shared/models/page';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskProjectDto, TaskSummaryDto } from './models';
import { TaskChanges } from '../../shared/task-changes';
import { TaskListPage } from './task-list-page';
import { aMember, aProject, aTask } from './task-testing';
import { TaskListQuery, TasksApi } from './tasks-api';

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

function row(key: string, extra: Partial<TaskSummaryDto> = {}): TaskSummaryDto {
  return {
    id: `id-${key}`,
    key,
    number: Number(key.split('-')[1]),
    title: `Titlul ${key}`,
    columnId: 'c1',
    columnName: 'De făcut',
    columnIsDone: false,
    priority: 'Medium',
    dueDate: null,
    assignees: [],
    completedAt: null,
    archivedAt: null,
    updatedAt: new Date(Date.now() - 60_000).toISOString(),
    ...extra,
  };
}

describe('TaskListPage', () => {
  let rows: TaskSummaryDto[];
  let project: TaskProjectDto;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const held = signal<string[]>(['tasks.create']);
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;
  let loader: HarnessLoader;

  beforeEach(() => {
    rows = [
      row('KAN-3', {
        title: 'Login SSO',
        priority: 'High',
        dueDate: new Date(Date.now() - 86_400_000).toISOString(),
        assignees: [
          { userId: 'u2', displayName: 'Ana Pop' },
          { userId: 'u3', displayName: 'Dan Ionescu' },
        ],
      }),
      row('KAN-2', { columnName: 'Gata', columnIsDone: true, completedAt: '2026-11-05T08:00:00Z' }),
      row('KAN-1'),
    ];
    project = aProject();
    held.set(['tasks.create']);
    api = {
      list: vi.fn((): Observable<Page<TaskSummaryDto>> =>
        of({ items: rows, page: 1, pageSize: 50, totalCount: rows.length }),
      ),
      project: vi.fn(() => of(project)),
      members: vi.fn(() => of([aMember('me', 'Florin Danci'), aMember('u2', 'Ana Pop')])),
      create: vi.fn(() => of(aTask({ key: 'KAN-4' }))),
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
                children: [{ path: 'list', component: TaskListPage }],
              },
              { path: 'task/:key', outlet: 'panel', component: PanelLike },
            ],
          },
        ]),
        { provide: TasksApi, useValue: api },
        {
          provide: AuthStore,
          useValue: {
            has: (name: string) => held().includes(name),
            user: () => ({ id: 'me' }),
          },
        },
        {
          provide: Confirmation,
          useValue: {
            ask: vi.fn(() => Promise.resolve(true)),
            discardChanges: vi.fn(() => Promise.resolve(true)),
          },
        },
        { provide: Snackbar, useValue: snackbar },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
  });

  async function visit(url = '/projects/p1/list'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create(url);
    loader = TestbedHarnessEnvironment.documentRootLoader(harness.fixture);
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

  function page(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function lastQuery(): TaskListQuery {
    return api['list'].mock.lastCall![1] as TaskListQuery;
  }

  function cell(key: string, column: string): HTMLElement {
    const found = [...page().querySelectorAll<HTMLElement>('tr.kb-table-row')].find(
      (tr) => tr.querySelector('td.key')?.textContent.trim() === key,
    )!;
    return found.querySelector<HTMLElement>(`td.${column}`)!;
  }

  function button(label: string): HTMLButtonElement | undefined {
    return [...page().querySelectorAll('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  function url(): string {
    return TestBed.inject(Router).url;
  }

  it('asks for the first page of the project, by key from the newest', async () => {
    await visit();

    expect(api['list']).toHaveBeenCalledWith('p1', {
      page: 1,
      pageSize: 50,
      sort: { field: 'key', descending: true },
      assigneeId: null,
      priorities: [],
      overdue: false,
      search: '',
      includeArchived: false,
    });
  });

  // docs/UI_UX_SPEC.md §7.3: key, title, column, priority, deadline, assignees, last change.
  it('shows each task with its key, title, column, priority, deadline, assignees and last change', async () => {
    await visit();

    expect(cell('KAN-3', 'title').textContent).toContain('Login SSO');
    expect(cell('KAN-3', 'column').textContent).toContain('De făcut');
    expect(cell('KAN-3', 'priority').querySelector('kb-chip')?.classList).toContain('high');
    expect(cell('KAN-3', 'due').querySelector('kb-chip')?.classList).toContain('deadline-overdue');
    const stack = cell('KAN-3', 'assignee').querySelector('kb-avatar-stack');
    expect(stack?.getAttribute('aria-label')).toBe('Ana Pop, Dan Ionescu');
    expect(stack?.querySelectorAll('kb-avatar').length).toBe(2);
    expect(page().querySelector('th.mat-column-assignee')?.textContent).toContain('Asignați');
    expect(cell('KAN-3', 'updated').textContent.trim()).toBe('acum 1 minut');
    expect(cell('KAN-1', 'assignee').textContent.trim()).toBe('Neasignat');
    expect(cell('KAN-2', 'column').querySelector('.done')).not.toBeNull();
  });

  it('reads the filters, the sort and the page from the address', async () => {
    await visit(
      '/projects/p1/list?assignee=me&priority=High,Critical&overdue=1&q=login&sort=title:asc&page=2&size=25',
    );

    expect(lastQuery()).toEqual({
      page: 2,
      pageSize: 25,
      sort: { field: 'title', descending: false },
      assigneeId: 'me',
      priorities: ['High', 'Critical'],
      overdue: true,
      search: 'login',
      includeArchived: false,
    });
  });

  it('puts a filter into the address, back on the first page', async () => {
    await visit('/projects/p1/list?page=3');

    button('Întârziate')!.click();
    await settle();

    expect(url()).toBe('/projects/p1/list?overdue=1');
    expect(lastQuery().overdue).toBe(true);
  });

  it('picks an assignee and priorities with the board filters', async () => {
    await visit();

    await (
      await loader.getHarness(MatSelectHarness.with({ selector: '.assignee' }))
    ).clickOptions({
      text: /Ana Pop/,
    });
    await settle();

    expect(url()).toBe('/projects/p1/list?assignee=u2');
    expect(lastQuery().assigneeId).toBe('u2');
  });

  it('sorts by a column, and turns the order around on a second click', async () => {
    await visit();

    page().querySelector<HTMLButtonElement>('th.mat-column-title button.sort')!.click();
    await settle();
    expect(url()).toBe('/projects/p1/list?sort=title:asc');
    expect(page().querySelector('th.mat-column-title')?.getAttribute('aria-sort')).toBe(
      'ascending',
    );

    page().querySelector<HTMLButtonElement>('th.mat-column-title button.sort')!.click();
    await settle();
    expect(url()).toBe('/projects/p1/list?sort=title:desc');
    expect(lastQuery().sort).toEqual({ field: 'title', descending: true });
  });

  it('opens a task in its panel, keeping the filters', async () => {
    await visit('/projects/p1/list?overdue=1');

    cell('KAN-3', 'title').querySelector<HTMLButtonElement>('button.open')!.click();
    await settle();

    expect(url()).toBe('/projects/p1/list(panel:task/KAN-3)?overdue=1');
  });

  it('reads the page again when the panel saves a change', async () => {
    await visit();
    const before = api['list'].mock.calls.length;

    TestBed.inject(TaskChanges).saved();
    await settle();

    expect(api['list'].mock.calls.length).toBe(before + 1);
  });

  it('offers archived tasks to tasks.archive, and marks them', async () => {
    held.set(['tasks.create', 'tasks.archive']);
    rows = [row('KAN-1', { archivedAt: '2026-11-05T08:00:00Z' })];
    await visit('/projects/p1/list?archived=1');

    expect(lastQuery().includeArchived).toBe(true);
    expect(cell('KAN-1', 'title').querySelector('kb-chip.archived')?.textContent.trim()).toBe(
      'Arhivat',
    );
  });

  it('asks for no archived tasks without tasks.archive, whatever the address says', async () => {
    await visit('/projects/p1/list?archived=1');

    expect(lastQuery().includeArchived).toBe(false);
    expect(button('Și cele arhivate')).toBeUndefined();
  });

  it('says there are no tasks yet, and offers the first one to whoever may create it', async () => {
    rows = [];
    await visit();

    expect(page().textContent).toContain('Niciun task încă');
    expect(button('Creează primul task')).toBeDefined();
  });

  it('says no task matches the filters, and clears them', async () => {
    rows = [];
    await visit('/projects/p1/list?overdue=1');

    expect(page().textContent).toContain('Niciun task nu se potrivește');
    [...page().querySelectorAll('kb-empty-state button')]
      .find((candidate) => candidate.textContent.trim() === 'Șterge filtrele')!
      .dispatchEvent(new MouseEvent('click'));
    await settle();

    expect(url()).toBe('/projects/p1/list');
  });

  it('offers Task nou only to a member with tasks.create, in an open project', async () => {
    await visit();
    expect(button('Task nou')).toBeDefined();
  });

  it('offers no Task nou without tasks.create', async () => {
    held.set([]);
    await visit();
    expect(button('Task nou')).toBeUndefined();
  });

  it('offers no Task nou to someone who only sees the project', async () => {
    project = { ...project, myRole: null };
    await visit();
    expect(button('Task nou')).toBeUndefined();
  });

  it('offers no Task nou in an archived project', async () => {
    project = { ...project, archivedAt: '2026-11-05T08:00:00Z' };
    await visit();
    expect(button('Task nou')).toBeUndefined();
  });

  it('creates a task from its sheet, says so and reads the page again', async () => {
    await visit();
    const before = api['list'].mock.calls.length;

    button('Task nou')!.click();
    await settle();
    const sheet = document.querySelector<HTMLElement>('kb-task-sheet')!;
    const title = sheet.querySelector<HTMLInputElement>('#task-title')!;
    title.value = '  Raport lunar ';
    title.dispatchEvent(new Event('input'));
    const assignees = await loader.getHarness(
      MatSelectHarness.with({ selector: '[aria-labelledby~="task-assignee"]' }),
    );
    expect(await assignees.isMultiple()).toBe(true);
    expect(await assignees.getValueText()).toBe('Neasignat');
    await assignees.clickOptions({ text: /Ana Pop/ });
    await assignees.clickOptions({ text: /Florin Danci/ });
    await closeList(assignees);
    await (
      await loader.getHarness(
        MatSelectHarness.with({ selector: '[aria-labelledby~="task-column"]' }),
      )
    ).clickOptions({ text: 'În lucru' });
    sheet.querySelector('form')!.requestSubmit();
    await settle();

    expect(api['create']).toHaveBeenCalledWith('p1', {
      title: 'Raport lunar',
      description: null,
      priority: 'Medium',
      columnId: 'c2',
      assigneeIds: ['me', 'u2'],
      dueDate: null,
    });
    expect(snackbar.show).toHaveBeenCalledWith('Task-ul KAN-4 a fost creat.');
    expect(api['list'].mock.calls.length).toBe(before + 1);
  });

  it('starts a new task in the first column, and refuses one without a title', async () => {
    await visit();

    button('Task nou')!.click();
    await settle();
    const sheet = document.querySelector<HTMLElement>('kb-task-sheet')!;
    expect(
      await (
        await loader.getHarness(
          MatSelectHarness.with({ selector: '[aria-labelledby~="task-column"]' }),
        )
      ).getValueText(),
    ).toBe('De făcut');
    sheet.querySelector('form')!.requestSubmit();
    await settle();

    expect(api['create']).not.toHaveBeenCalled();
    expect(sheet.querySelector('#task-title-error')?.textContent).toBe('Scrie titlul task-ului.');
  });
});

/**
 * Closes a list as a person does, with Esc on its trigger. MatSelectHarness.close clicks the first
 * backdrop of the page, which in a sheet or the panel is the dialog's, and closes that instead.
 */
async function closeList(select: MatSelectHarness): Promise<void> {
  await (await select.host()).sendKeys(TestKey.ESCAPE);
}
