import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Page } from '../../shared/models/page';
import { TaskChanges } from '../../shared/task-changes';
import { MyTaskDto } from './models';
import { MyTasksPage } from './my-tasks-page';
import { MyTasksQuery, TasksApi } from './tasks-api';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet /><router-outlet name="panel" />`,
})
class ShellLike {}

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class PanelLike {}

function row(key: string, extra: Partial<MyTaskDto> = {}): MyTaskDto {
  const [projectKey, number] = key.split('-');
  return {
    id: `id-${key}`,
    projectId: `p-${projectKey}`,
    projectKey,
    projectName: projectKey === 'KAN' ? 'Kanabol' : 'Site',
    key,
    number: Number(number),
    title: `Titlul ${key}`,
    columnId: 'c1',
    columnName: 'De făcut',
    columnIsDone: false,
    priority: 'Medium',
    dueDate: null,
    assignees: [{ userId: 'me', displayName: 'Florin Danci' }],
    completedAt: null,
    archivedAt: null,
    updatedAt: new Date(Date.now() - 60_000).toISOString(),
    ...extra,
  };
}

/**
 * docs/UI_UX_SPEC.md §7.2: "Task-urile mele" at /my-tasks, the project's List view across every project,
 * with a "Proiect" column and no assignee select, only the open ones unless "Și cele terminate" says
 * otherwise, by deadline from the soonest, 50 to a page, the filters in the address.
 */
describe('MyTasksPage', () => {
  let rows: MyTaskDto[];
  let api: {
    myTasks: ReturnType<typeof vi.fn<(query: MyTasksQuery) => Observable<Page<MyTaskDto>>>>;
  };
  const held = signal<string[]>([]);
  let harness: RouterTestingHarness;
  let loader: HarnessLoader;

  beforeEach(() => {
    rows = [
      row('KAN-3', {
        title: 'Login SSO',
        priority: 'High',
        dueDate: new Date(Date.now() - 86_400_000).toISOString(),
      }),
      row('WEB-1', { title: 'Formular de contact' }),
    ];
    held.set([]);
    api = {
      myTasks: vi.fn(() => of({ items: rows, page: 1, pageSize: 50, totalCount: rows.length })),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: ShellLike,
            children: [
              { path: 'my-tasks', component: MyTasksPage },
              { path: 'task/:key', outlet: 'panel', component: PanelLike },
            ],
          },
        ]),
        { provide: TasksApi, useValue: api },
        {
          provide: AuthStore,
          useValue: { has: (name: string) => held().includes(name), user: () => ({ id: 'me' }) },
        },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function visit(url = '/my-tasks'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create(url);
    loader = TestbedHarnessEnvironment.documentRootLoader(harness.fixture);
    await settle();
    return page();
  }

  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await new Promise((resolve) => setTimeout(resolve));
      harness.fixture.detectChanges();
      await harness.fixture.whenStable();
    }
  }

  function page(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function lastQuery(): MyTasksQuery {
    return api.myTasks.mock.lastCall![0];
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

  it('is titled "Task-urile mele", under Acasă', async () => {
    await visit();

    expect(page().querySelector('h1')?.textContent.trim()).toBe('Task-urile mele');
    expect(page().querySelector('kb-page-header nav')?.textContent).toContain('Acasă');
  });

  it('reads the first page of 50 of my open tasks, by deadline from the soonest', async () => {
    await visit();

    expect(api.myTasks).toHaveBeenCalledWith({
      page: 1,
      pageSize: 50,
      sort: { field: 'dueDate', descending: false },
      open: true,
      priorities: [],
      overdue: false,
      search: '',
      includeArchived: false,
    });
    expect(page().querySelector('th.mat-column-dueDate')?.getAttribute('aria-sort')).toBe(
      'ascending',
    );
  });

  it('shows the list\'s table with a "Proiect" column, and no assignee select', async () => {
    await visit();

    expect([...page().querySelectorAll('th')].map((th) => th.textContent.trim())).toEqual([
      'Cheieexpand_more',
      'Titluexpand_more',
      'Proiect',
      'Coloanăexpand_more',
      'Prioritateexpand_more',
      'Deadlineexpand_more',
      'Asignațiexpand_more',
      'Actualizatexpand_more',
    ]);
    expect(cell('WEB-1', 'project').textContent.trim()).toBe('Site');
    expect(cell('KAN-3', 'due').querySelector('kb-chip')?.classList).toContain('deadline-overdue');
    expect(page().querySelector('kb-task-filter-bar mat-select.assignee')).toBeNull();
    expect(button('Ale mele')).toBeUndefined();
  });

  it('takes the filters from the address: q, priority, overdue=1, done=1 and archived=1', async () => {
    held.set(['tasks.archive']);
    await visit(
      '/my-tasks?q=login&priority=High,Critical&overdue=1&done=1&archived=1&sort=key:desc&page=2&size=25',
    );

    expect(lastQuery()).toEqual({
      page: 2,
      pageSize: 25,
      sort: { field: 'key', descending: true },
      open: false,
      priorities: ['High', 'Critical'],
      overdue: true,
      search: 'login',
      includeArchived: true,
    });
    expect(button('Și cele terminate')?.getAttribute('aria-pressed')).toBe('true');
    expect(button('Întârziate')?.getAttribute('aria-pressed')).toBe('true');
  });

  // The main panel's "Întârziate" tile leads here.
  it('opens on the overdue ones from /my-tasks?overdue=1', async () => {
    await visit('/my-tasks?overdue=1');

    expect(lastQuery().overdue).toBe(true);
    expect(lastQuery().open).toBe(true);
  });

  it('puts "Și cele terminate" in the address and back, from the first page', async () => {
    await visit('/my-tasks?page=3');

    button('Și cele terminate')!.click();
    await settle();
    expect(url()).toBe('/my-tasks?done=1');
    expect(lastQuery().open).toBe(false);

    button('Și cele terminate')!.click();
    await settle();
    expect(url()).toBe('/my-tasks');
    expect(lastQuery().open).toBe(true);
  });

  it('offers "Și cele arhivate" only to tasks.archive, and asks for none without it', async () => {
    await visit('/my-tasks?archived=1');

    expect(button('Și cele arhivate')).toBeUndefined();
    expect(lastQuery().includeArchived).toBe(false);
  });

  it('sorts by a column through the address, and leaves the deadline out of it', async () => {
    await visit();

    page().querySelector<HTMLButtonElement>('th.mat-column-title button.sort')!.click();
    await settle();
    expect(url()).toBe('/my-tasks?sort=title:asc');

    page().querySelector<HTMLButtonElement>('th.mat-column-dueDate button.sort')!.click();
    await settle();
    expect(url()).toBe('/my-tasks');
    expect(lastQuery().sort).toEqual({ field: 'dueDate', descending: false });
  });

  it('pages through the address, 50 by default', async () => {
    rows = Array.from({ length: 50 }, (_, index) => row(`KAN-${String(index + 1)}`));
    api.myTasks.mockImplementation(() =>
      of({ items: rows, page: 1, pageSize: 50, totalCount: 120 }),
    );
    await visit();

    await (await loader.getHarness(MatPaginatorHarness)).goToNextPage();
    await settle();

    expect(url()).toBe('/my-tasks?page=2');
    expect(lastQuery().page).toBe(2);
  });

  it('opens a task in its panel, keeping the filters', async () => {
    await visit('/my-tasks?overdue=1');

    cell('KAN-3', 'title').querySelector<HTMLButtonElement>('button.open')!.click();
    await settle();

    expect(url()).toBe('/my-tasks(panel:task/KAN-3)?overdue=1');
  });

  it('reads the page again when the panel saves a change', async () => {
    await visit();
    const before = api.myTasks.mock.calls.length;

    TestBed.inject(TaskChanges).saved();
    await settle();

    expect(api.myTasks.mock.calls.length).toBe(before + 1);
  });

  it('says "Niciun task deschis." without filters', async () => {
    rows = [];
    await visit('/my-tasks?done=1');

    expect(page().querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Niciun task deschis.',
    );
    expect(button('Șterge filtrele')).toBeUndefined();
  });

  it('says "Niciun task pentru filtrele alese." with them, and clears them, keeping the finished ones', async () => {
    rows = [];
    await visit('/my-tasks?overdue=1&q=x&done=1');

    expect(page().querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Niciun task pentru filtrele alese.',
    );
    [...page().querySelectorAll('kb-empty-state button')]
      .find((candidate) => candidate.textContent.trim() === 'Șterge filtrele')!
      .dispatchEvent(new MouseEvent('click'));
    await settle();

    expect(url()).toBe('/my-tasks?done=1');
  });

  it('says it could not read the tasks, and tries again', async () => {
    api.myTasks.mockReturnValueOnce(throwError(() => new Error('down')));
    await visit();

    expect(page().querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Nu am putut încărca task-urile.',
    );
    button('Reîncearcă')!.click();
    await settle();

    expect(page().querySelectorAll('tr.kb-table-row')).toHaveLength(2);
  });
});
