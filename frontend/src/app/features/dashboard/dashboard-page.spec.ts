import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { BellStore } from '../../core/notifications/bell-store';
import { NotificationDto, NotificationQuery } from '../../core/notifications/models';
import { NotificationsApi } from '../../core/notifications/notifications-api';
import { aNotification } from '../../core/notifications/notifications-testing';
import { Page } from '../../shared/models/page';
import { TaskChanges } from '../../shared/task-changes';
import { DashboardApi } from './dashboard-api';
import { DashboardPage } from './dashboard-page';
import { aDashboardProject, aDashboardTask, aSummary } from './dashboard-testing';
import { DashboardProjectDto, DashboardTaskDto, MySummaryDto } from './models';
import { weekStart } from './week-start';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet /><router-outlet name="panel" />`,
})
class ShellLike {}

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class Elsewhere {}

function pageOf<T>(items: T[]): Page<T> {
  return { items, page: 1, pageSize: items.length, totalCount: items.length };
}

/**
 * docs/UI_UX_SPEC.md §7.2: the main panel's four tiles, "Task-urile mele", "Notificări recente" and
 * "Proiectele mele", each read on its own, with its skeleton, its empty text and its error, read when the
 * page opens, without SignalR, and the tiles and tasks read again after a save from the task panel.
 */
describe('DashboardPage', () => {
  let api: {
    summary: ReturnType<typeof vi.fn<(weekStart: string) => Observable<MySummaryDto>>>;
    openTasks: ReturnType<typeof vi.fn<(count: number) => Observable<Page<DashboardTaskDto>>>>;
    myProjects: ReturnType<typeof vi.fn<() => Observable<Page<DashboardProjectDto>>>>;
  };
  let notifications: {
    list: ReturnType<
      typeof vi.fn<(query: NotificationQuery, quiet?: boolean) => Observable<Page<NotificationDto>>>
    >;
    markRead: ReturnType<typeof vi.fn<(id: string) => Observable<NotificationDto>>>;
  };
  let bell: { readAgain: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;

  beforeEach(() => {
    api = {
      summary: vi.fn(() => of(aSummary())),
      openTasks: vi.fn(() =>
        of(
          pageOf([
            aDashboardTask(),
            aDashboardTask({
              id: 't3',
              key: 'WEB-3',
              title: 'Formular de contact',
              projectId: 'p2',
              projectKey: 'WEB',
              projectName: 'Site',
              priority: 'Critical',
              dueDate: null,
            }),
          ]),
        ),
      ),
      myProjects: vi.fn(() =>
        of(
          pageOf([
            aDashboardProject(),
            aDashboardProject({
              id: 'p2',
              name: 'Site',
              key: 'WEB',
              owners: [
                { userId: 'u1', displayName: 'Ana Pop' },
                { userId: 'u2', displayName: 'Dan Ionescu' },
              ],
              openTaskCount: 1,
              completedTaskCount: 0,
            }),
          ]),
        ),
      ),
    };
    notifications = {
      list: vi.fn(() =>
        of(pageOf([aNotification(), aNotification({ id: 'n2', readAt: '2026-11-08T08:00:00Z' })])),
      ),
      markRead: vi.fn((id: string) => of(aNotification({ id, readAt: new Date().toISOString() }))),
    };
    bell = { readAgain: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: ShellLike,
            children: [
              { path: '', component: DashboardPage },
              { path: 'my-tasks', component: Elsewhere },
              { path: 'notifications', component: Elsewhere },
              { path: 'projects', component: Elsewhere },
              { path: 'projects/:id/board', component: Elsewhere },
              { path: 'task/:key', outlet: 'panel', component: Elsewhere },
            ],
          },
        ]),
        { provide: DashboardApi, useValue: api },
        { provide: NotificationsApi, useValue: notifications },
        { provide: BellStore, useValue: bell },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function visit(): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create('/');
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

  function section(name: 'tiles' | 'tasks' | 'notifications' | 'projects'): HTMLElement {
    return page().querySelector<HTMLElement>(`.${name}`)!;
  }

  function texts(root: Element, selector: string): string[] {
    return [...root.querySelectorAll(selector)].map((found) =>
      found.textContent.trim().replace(/\s+/g, ' '),
    );
  }

  function button(root: Element, label: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find((found) => found.textContent.trim() === label);
  }

  function url(): string {
    return TestBed.inject(Router).url;
  }

  it('is titled "Panou principal", without a breadcrumb', async () => {
    await visit();

    expect(page().querySelector('h1')?.textContent.trim()).toBe('Panou principal');
    expect(page().querySelector('kb-page-header nav')).toBeNull();
  });

  it("reads the summary from Monday at midnight in the person's time", async () => {
    await visit();

    expect(api.summary).toHaveBeenCalledWith(weekStart(new Date()));
  });

  it('shows the four tiles with their numbers and fixed captions', async () => {
    await visit();
    const tiles = section('tiles');

    expect(texts(tiles, 'kb-stat-tile .label')).toEqual([
      'Task-urile mele deschise',
      'Întârziate',
      'Finalizate săptămâna asta',
      'Proiecte active',
    ]);
    expect(texts(tiles, 'kb-stat-tile .value')).toEqual(['7', '2', '3', '4']);
    expect(texts(tiles, 'kb-stat-tile .caption')).toEqual([
      'asignate mie',
      'după deadline',
      'de luni încoace',
      'în care sunt membru',
    ]);
  });

  it('leads the tiles to /my-tasks, /my-tasks?overdue=1 and /projects, and "Finalizate" nowhere', async () => {
    await visit();
    const tiles = [...section('tiles').querySelectorAll('kb-stat-tile')];

    expect(tiles.map((tile) => tile.querySelector('a')?.getAttribute('href') ?? null)).toEqual([
      '/my-tasks',
      '/my-tasks?overdue=1',
      null,
      '/projects',
    ]);
  });

  it('lists the first 8 open tasks by deadline, with key, title, project, deadline and priority', async () => {
    await visit();
    const rows = [...section('tasks').querySelectorAll('tr.kb-table-row')];

    expect(api.openTasks).toHaveBeenCalledWith(8);
    expect(rows.map((row) => texts(row, '.key')[0])).toEqual(['KAN-12', 'WEB-3']);
    expect(texts(rows[0], '.title')).toEqual(['Login SSO']);
    expect(texts(rows[1], '.project')).toEqual(['Site']);
    expect(rows[0].querySelector('.due kb-deadline-chip')).not.toBeNull();
    expect(rows[1].querySelector('.due kb-deadline-chip')).toBeNull();
    expect(texts(rows[1], '.priority kb-chip')[0]).toMatch(/Critical$/);
  });

  it("opens a task's panel over the main panel from its row", async () => {
    await visit();

    section('tasks').querySelector<HTMLElement>('tr.kb-table-row')!.click();
    await settle();

    expect(url()).toBe('/(panel:task/KAN-12)');
  });

  it('says "Niciun task deschis." without open tasks', async () => {
    api.openTasks.mockReturnValue(of(pageOf<DashboardTaskDto>([])));
    await visit();

    expect(texts(section('tasks'), 'kb-empty-state h3')).toEqual(['Niciun task deschis.']);
  });

  it('shows the last 5 notifications with their lines', async () => {
    await visit();

    expect(notifications.list).toHaveBeenCalledWith({
      page: 1,
      pageSize: 5,
      unread: false,
      type: null,
    });
    expect(texts(section('notifications'), 'button.row .text')).toEqual([
      'KAN-12: Login SSO a depășit deadline-ul',
      'KAN-12: Login SSO a depășit deadline-ul',
    ]);
    expect(
      [...section('notifications').querySelectorAll('button.row')].map((row) =>
        row.classList.contains('unread'),
      ),
    ).toEqual([true, false]);
  });

  it('marks a notification read, tells the bell and opens its task', async () => {
    await visit();

    section('notifications').querySelector<HTMLButtonElement>('button.row')!.click();
    await settle();

    expect(notifications.markRead).toHaveBeenCalledWith('n1');
    expect(bell.readAgain).toHaveBeenCalled();
    expect(section('notifications').querySelector('button.row')?.classList.contains('unread')).toBe(
      false,
    );
    expect(url()).toBe('/(panel:task/KAN-12)');
  });

  it('opens a notification read before without marking it again', async () => {
    await visit();

    section('notifications').querySelectorAll<HTMLButtonElement>('button.row')[1].click();
    await settle();

    expect(notifications.markRead).not.toHaveBeenCalled();
    expect(url()).toBe('/(panel:task/KAN-12)');
  });

  it('says "Nicio notificare." without notifications', async () => {
    notifications.list.mockReturnValue(of(pageOf<NotificationDto>([])));
    await visit();

    expect(texts(section('notifications'), 'kb-empty-state h3')).toEqual(['Nicio notificare.']);
  });

  it('shows my projects as cards: name and key, owners, "3 din 12 gata" and the open tasks', async () => {
    await visit();
    const cards = [...section('projects').querySelectorAll<HTMLAnchorElement>('a.project')];

    expect(cards.map((card) => card.getAttribute('href'))).toEqual([
      '/projects/p1/board',
      '/projects/p2/board',
    ]);
    expect(texts(cards[0], '.name')).toEqual(['Kanabol']);
    expect(texts(cards[0], '.key')).toEqual(['KAN']);
    expect(texts(cards[1], '.owners')).toEqual(['Ana Pop, Dan Ionescu']);
    expect(texts(cards[0], '.done')).toEqual(['3 din 12 gata']);
    expect(texts(cards[0], '.open')).toEqual(['9 task-uri deschise']);
    expect(texts(cards[1], '.open')).toEqual(['1 task deschis']);
    expect(cards[0].querySelector<HTMLElement>('.bar span')?.style.width).toBe('25%');
    expect(cards[1].querySelector<HTMLElement>('.bar span')?.style.width).toBe('0%');
  });

  it('says "Nu ești în niciun proiect încă." without projects', async () => {
    api.myProjects.mockReturnValue(of(pageOf<DashboardProjectDto>([])));
    await visit();

    expect(texts(section('projects'), 'kb-empty-state h3')).toEqual([
      'Nu ești în niciun proiect încă.',
    ]);
  });

  it('leads "Vezi toate" to /my-tasks, /notifications and /projects', async () => {
    await visit();

    expect([...page().querySelectorAll('a.all')].map((link) => link.getAttribute('href'))).toEqual([
      '/my-tasks',
      '/notifications',
      '/projects',
    ]);
  });

  it('shows a skeleton in each section while it reads', async () => {
    api.summary.mockReturnValue(NEVER);
    api.openTasks.mockReturnValue(NEVER);
    api.myProjects.mockReturnValue(NEVER);
    notifications.list.mockReturnValue(NEVER);
    await visit();

    for (const name of ['tasks', 'notifications', 'projects'] as const) {
      expect(section(name).querySelector('[aria-busy="true"] kb-skeleton'), name).not.toBeNull();
    }
    expect(section('tiles').querySelectorAll('kb-stat-tile .value kb-skeleton')).toHaveLength(4);
  });

  it('says each section could not be read, and tries that one again', async () => {
    api.summary.mockReturnValue(throwError(() => new Error('down')));
    api.openTasks.mockReturnValue(throwError(() => new Error('down')));
    notifications.list.mockReturnValue(throwError(() => new Error('down')));
    api.myProjects.mockReturnValue(throwError(() => new Error('down')));
    await visit();

    expect(texts(section('tiles'), 'kb-empty-state h3')).toEqual(['Nu am putut încărca sumarul.']);
    expect(texts(section('tasks'), 'kb-empty-state h3')).toEqual([
      'Nu am putut încărca task-urile.',
    ]);
    expect(texts(section('notifications'), 'kb-empty-state h3')).toEqual([
      'Nu am putut încărca notificările.',
    ]);
    expect(texts(section('projects'), 'kb-empty-state h3')).toEqual([
      'Nu am putut încărca proiectele.',
    ]);

    api.openTasks.mockReturnValue(of(pageOf([aDashboardTask()])));
    button(section('tasks'), 'Reîncearcă')!.click();
    await settle();

    expect(section('tasks').querySelectorAll('tr.kb-table-row')).toHaveLength(1);
    expect(api.summary).toHaveBeenCalledTimes(1);
    expect(texts(section('tiles'), 'kb-empty-state h3')).toEqual(['Nu am putut încărca sumarul.']);
  });

  it('reads the tiles and the tasks again after a save from the task panel, and nothing else', async () => {
    await visit();

    TestBed.inject(TaskChanges).saved();
    await settle();

    expect(api.summary).toHaveBeenCalledTimes(2);
    expect(api.openTasks).toHaveBeenCalledTimes(2);
    expect(api.myProjects).toHaveBeenCalledTimes(1);
    expect(notifications.list).toHaveBeenCalledTimes(1);
  });

  // The rows stay while they're read again, so the table doesn't blink behind the panel.
  it('keeps the tasks it shows while it reads them again', async () => {
    await visit();
    api.openTasks.mockReturnValue(NEVER);

    TestBed.inject(TaskChanges).saved();
    await settle();

    expect(section('tasks').querySelectorAll('tr.kb-table-row')).toHaveLength(2);
    expect(texts(section('tiles'), 'kb-stat-tile .value')).toEqual(['7', '2', '3', '4']);
  });
});
