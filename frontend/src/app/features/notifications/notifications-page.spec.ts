import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { BellStore } from '../../core/notifications/bell-store';
import {
  NotificationCountDto,
  NotificationDto,
  NotificationQuery,
} from '../../core/notifications/models';
import { NotificationsApi } from '../../core/notifications/notifications-api';
import { aNotification } from '../../core/notifications/notifications-testing';
import { Page } from '../../shared/models/page';
import { NotificationsPage } from './notifications-page';

/**
 * docs/UI_UX_SPEC.md §7.6: /notifications, the bell's rows 50 to a page, the filters unread=1 and type= in
 * the address, "Marchează toate ca citite", and a row that marks its notification read and opens its task.
 */
describe('NotificationsPage', () => {
  let items: NotificationDto[];
  let total: number;
  let api: {
    list: ReturnType<
      typeof vi.fn<(query: NotificationQuery, quiet?: boolean) => Observable<Page<NotificationDto>>>
    >;
    markRead: ReturnType<typeof vi.fn<(id: string) => Observable<NotificationDto>>>;
    markAllRead: ReturnType<typeof vi.fn<() => Observable<NotificationCountDto>>>;
  };
  const unread = signal(2);
  let bell: { count: typeof unread; readAgain: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;
  let loader: HarnessLoader;

  beforeEach(() => {
    items = [
      aNotification({ id: 'n3', type: 'TaskAssigned' }),
      aNotification({ id: 'n2', type: 'CommentAdded', readAt: '2026-11-08T08:00:00Z' }),
      aNotification({ id: 'n1', task: null, message: 'Without a task' }),
    ];
    total = items.length;
    unread.set(2);
    api = {
      list: vi.fn(() => of({ items, page: 1, pageSize: 50, totalCount: total })),
      markRead: vi.fn((id: string) =>
        of({ ...items.find((item) => item.id === id)!, readAt: new Date().toISOString() }),
      ),
      markAllRead: vi.fn(() => of({ count: 2 })),
    };
    bell = { count: unread, readAgain: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'notifications', component: NotificationsPage },
          { path: 'tasks/:key', children: [] },
        ]),
        { provide: NotificationsApi, useValue: api },
        { provide: BellStore, useValue: bell },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function visit(url = '/notifications'): Promise<HTMLElement> {
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

  function rows(): HTMLButtonElement[] {
    return [...page().querySelectorAll<HTMLButtonElement>('button.row')];
  }

  function button(label: string): HTMLButtonElement | undefined {
    return [...page().querySelectorAll('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  function lastQuery(): NotificationQuery {
    return api.list.mock.lastCall![0];
  }

  function url(): string {
    return TestBed.inject(Router).url;
  }

  it('reads the first page of 50, every type, read or not', async () => {
    await visit();

    expect(api.list).toHaveBeenCalledWith({ page: 1, pageSize: 50, unread: false, type: null });
    expect(page().querySelector('h1')?.textContent.trim()).toBe('Notificări');
  });

  it("shows the bell's rows: the text from type and task, unread ones tinted", async () => {
    await visit();

    expect(rows().map((row) => row.querySelector('.text')?.textContent.trim())).toEqual([
      'Ai fost asignat pe KAN-12: Login SSO',
      'Comentariu nou pe KAN-12: Login SSO',
      'Without a task',
    ]);
    expect(rows().map((row) => row.classList.contains('unread'))).toEqual([true, false, true]);
    expect(rows()[0].querySelector('.icon')?.textContent.trim()).toBe('person_add');
  });

  it('takes the filters from the address: unread=1 and type=', async () => {
    await visit('/notifications?unread=1&type=CommentAdded&page=2&size=25');

    expect(lastQuery()).toEqual({ page: 2, pageSize: 25, unread: true, type: 'CommentAdded' });
    expect(page().querySelector('.kb-filter-chip')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('ignores a type it does not know, and a page or size that is not one', async () => {
    await visit('/notifications?type=Later&page=0&size=7');

    expect(lastQuery()).toEqual({ page: 1, pageSize: 50, unread: false, type: null });
  });

  it('puts "Doar necitite" in the address and back, from the first page', async () => {
    await visit('/notifications?page=3');

    button('Doar necitite')!.click();
    await settle();
    expect(url()).toBe('/notifications?unread=1');
    expect(lastQuery().unread).toBe(true);

    button('Doar necitite')!.click();
    await settle();
    expect(url()).toBe('/notifications');
  });

  it('chooses a type in its select, Toate taking it away', async () => {
    await visit();
    const select = await loader.getHarness(MatSelectHarness.with({ selector: '.type' }));

    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map((option) => option.getText()))).toEqual([
      'Toate',
      'Deadline',
      'Asignări',
      'Comentarii',
    ]);
    await select.clickOptions({ text: 'Deadline' });
    await settle();
    expect(url()).toBe('/notifications?type=TaskOverdue');
    expect(lastQuery().type).toBe('TaskOverdue');

    await select.open();
    await select.clickOptions({ text: 'Toate' });
    await settle();
    expect(url()).toBe('/notifications');
  });

  it('pages through the paginator, 50 by default', async () => {
    total = 120;
    await visit();
    const paginator = await loader.getHarness(MatPaginatorHarness);

    expect(await paginator.getPageSize()).toBe(50);
    await paginator.goToNextPage();
    await settle();

    expect(url()).toBe('/notifications?page=2');
    expect(lastQuery().page).toBe(2);
  });

  it('marks a row read and opens its task, and tells the bell', async () => {
    await visit();

    rows()[0].click();
    await settle();

    expect(api.markRead).toHaveBeenCalledWith('n3');
    expect(url()).toBe('/tasks/KAN-12');
    expect(bell.readAgain).toHaveBeenCalled();
  });

  it('opens a read one without marking it, and marks one without a task without leaving', async () => {
    await visit();

    rows()[2].click();
    await settle();
    expect(api.markRead).toHaveBeenCalledWith('n1');
    expect(url()).toBe('/notifications');

    rows()[1].click();
    await settle();
    expect(api.markRead).toHaveBeenCalledTimes(1);
    expect(url()).toBe('/tasks/KAN-12');
  });

  it('marks every one read, then reads the page and the bell again; off with nothing unread', async () => {
    await visit();
    const markAll = button('Marchează toate ca citite')!;
    expect(markAll.disabled).toBe(false);

    markAll.click();
    await settle();

    expect(api.markAllRead).toHaveBeenCalledTimes(1);
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(bell.readAgain).toHaveBeenCalled();

    unread.set(0);
    await settle();
    expect(markAll.disabled).toBe(true);
  });

  it('says "Nicio notificare." without filters', async () => {
    items = [];
    total = 0;
    const element = await visit();

    expect(element.querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Nicio notificare.',
    );
    expect(rows()).toEqual([]);
  });

  it('says "Nicio notificare pentru filtrele alese." with them, and offers to clear them', async () => {
    items = [];
    total = 0;
    const element = await visit('/notifications?unread=1&type=TaskOverdue');

    expect(element.querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Nicio notificare pentru filtrele alese.',
    );
    button('Șterge filtrele')!.click();
    await settle();
    expect(url()).toBe('/notifications');
  });

  it('says it could not read them, and tries again', async () => {
    api.list.mockReturnValueOnce(throwError(() => new Error('offline')));
    const element = await visit();

    expect(element.querySelector('kb-empty-state h3')?.textContent.trim()).toBe(
      'Nu am putut încărca notificările.',
    );
    button('Reîncearcă')!.click();
    await settle();

    expect(rows()).toHaveLength(3);
  });
});
