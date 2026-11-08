import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthStore } from '../auth/auth-store';
import { Logger } from '../logging/logger';
import { FakeRealtime, provideFakeRealtime } from '../realtime/realtime-testing';
import { Page } from '../../shared/models/page';
import { BellStore } from './bell-store';
import { NotificationCountDto, NotificationDto } from './models';
import { NotificationBell } from './notification-bell';
import { NotificationsApi } from './notifications-api';
import { aNotification } from './notifications-testing';

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class Blank {}

function page(items: NotificationDto[]): Page<NotificationDto> {
  return { items, page: 1, pageSize: 10, totalCount: items.length };
}

/**
 * docs/UI_UX_SPEC.md §7.6, docs/SPEC.md §9: the bell's badge, read once signed in and again at each
 * NotificationCreated and reconnection, through Realtime loaded with import(); the menu's last 10.
 */
describe('NotificationBell', () => {
  const signedIn = signal(true);
  const mustChangePassword = signal(false);
  let unread: number;
  let items: NotificationDto[];
  let api: {
    unreadCount: ReturnType<typeof vi.fn<() => Observable<NotificationCountDto>>>;
    list: ReturnType<typeof vi.fn<(...args: unknown[]) => Observable<Page<NotificationDto>>>>;
    markRead: ReturnType<typeof vi.fn<(id: string) => Observable<NotificationDto>>>;
    markAllRead: ReturnType<typeof vi.fn<() => Observable<NotificationCountDto>>>;
  };
  let realtime: FakeRealtime;
  let fixture: ComponentFixture<NotificationBell>;

  beforeEach(() => {
    signedIn.set(true);
    mustChangePassword.set(false);
    unread = 2;
    items = [
      aNotification({ id: 'n2', type: 'CommentAdded' }),
      aNotification({
        id: 'n1',
        readAt: '2026-11-08T08:00:00Z',
        task: { id: 't3', key: 'KAN-3', title: 'Export' },
      }),
    ];
    api = {
      unreadCount: vi.fn(() => of({ count: unread })),
      list: vi.fn(() => of(page(items))),
      markRead: vi.fn((id: string) =>
        of({ ...items.find((item) => item.id === id)!, readAt: new Date().toISOString() }),
      ),
      markAllRead: vi.fn(() => of({ count: unread })),
    };
    realtime = new FakeRealtime();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', component: Blank }]),
        provideFakeRealtime(realtime),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: NotificationsApi, useValue: api },
        { provide: AuthStore, useValue: { isSignedIn: signedIn, mustChangePassword } },
        { provide: Logger, useValue: { warn: vi.fn(), debug: vi.fn() } },
      ],
    });
  });

  async function bell(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(NotificationBell);
    await fixture.whenStable();
    await vi.waitFor(() => {
      expect(realtime.opened).toBe(1);
    });
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  /** Lets the answers land, then the view follow: an event starts the reads without awaiting them. */
  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function openMenu(): Promise<MatMenuHarness> {
    const menu = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatMenuHarness);
    await menu.open();
    await fixture.whenStable();
    return menu;
  }

  function panel(): HTMLElement {
    const found = document.querySelector<HTMLElement>('.kb-notifications-menu');
    expect(found).not.toBeNull();
    return found!;
  }

  function rows(): HTMLElement[] {
    return [...panel().querySelectorAll<HTMLElement>('button.row')];
  }

  function badge(element: HTMLElement): string | null {
    return element.querySelector('.badge')?.textContent.trim() ?? null;
  }

  it('reads the unread count once signed in, and shows it on the bell', async () => {
    const element = await bell();

    expect(api.unreadCount).toHaveBeenCalledTimes(1);
    expect(badge(element)).toBe('2');
    expect(element.querySelector('button')?.getAttribute('aria-label')).toBe(
      'Notificări, 2 necitite',
    );
  });

  it('hides the badge at 0, and shows 99+ over 99', async () => {
    unread = 0;
    const element = await bell();
    expect(badge(element)).toBeNull();
    expect(element.querySelector('button')?.getAttribute('aria-label')).toBe('Notificări');

    unread = 1;
    realtime.send('NotificationCreated', { notificationId: 'n8' });
    await settle();
    expect(badge(element)).toBe('1');
    expect(element.querySelector('button')?.getAttribute('aria-label')).toBe(
      'Notificări, una necitită',
    );

    unread = 140;
    realtime.send('NotificationCreated', { notificationId: 'n9' });
    await settle();
    expect(badge(element)).toBe('99+');
    expect(element.querySelector('button')?.getAttribute('aria-label')).toBe(
      'Notificări, 140 necitite',
    );
  });

  it("opens the hub for the user's own group, without joining any project", async () => {
    await bell();

    expect(realtime.joined).toEqual([]);
    expect(realtime.listening('NotificationCreated')).toBe(1);
  });

  it('reads the count again at each NotificationCreated and each reconnection', async () => {
    const element = await bell();

    unread = 3;
    realtime.send('NotificationCreated', { notificationId: 'n3' });
    await settle();
    expect(badge(element)).toBe('3');

    unread = 5;
    realtime.reconnect();
    await settle();
    expect(badge(element)).toBe('5');
    expect(api.unreadCount).toHaveBeenCalledTimes(3);
  });

  it('reads the last 10 when the menu opens, quietly, from the newest', async () => {
    await bell();

    await openMenu();

    expect(api.list).toHaveBeenCalledWith(
      { page: 1, pageSize: 10, unread: false, type: null },
      true,
    );
    expect(rows().map((row) => row.querySelector('.text')?.textContent.trim())).toEqual([
      'Comentariu nou pe KAN-12: Login SSO',
      'KAN-3: Export a depășit deadline-ul',
    ]);
    expect(rows().map((row) => row.classList.contains('unread'))).toEqual([true, false]);
  });

  it('reads the list again each time it opens, and at NotificationCreated only while open', async () => {
    await bell();
    const menu = await openMenu();
    realtime.send('NotificationCreated', { notificationId: 'n3' });
    await settle();
    expect(api.list).toHaveBeenCalledTimes(2);

    await menu.close();
    realtime.send('NotificationCreated', { notificationId: 'n4' });
    await settle();
    expect(api.list).toHaveBeenCalledTimes(2);

    await menu.open();
    expect(api.list).toHaveBeenCalledTimes(3);
  });

  it('says so when there is none', async () => {
    items = [];
    await bell();

    await openMenu();

    expect(rows()).toEqual([]);
    expect(panel().textContent).toContain('Nicio notificare.');
  });

  it('says it could not read the list, and tries again', async () => {
    api.list.mockReturnValueOnce(throwError(() => new Error('offline')));
    await bell();

    await openMenu();
    expect(panel().textContent).toContain('Nu am putut încărca notificările.');
    const retry = [...panel().querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent.trim() === 'Reîncearcă',
    );
    retry!.click();
    await settle();

    expect(rows()).toHaveLength(2);
  });

  it('marks a row read and opens its task', async () => {
    await bell();
    await openMenu();
    unread = 1;

    rows()[0].click();
    await settle();

    expect(api.markRead).toHaveBeenCalledWith('n2');
    expect(TestBed.inject(Router).url).toBe('/tasks/KAN-12');
    expect(api.unreadCount).toHaveBeenCalledTimes(2);
  });

  it('opens a read one without marking it again', async () => {
    await bell();
    await openMenu();

    rows()[1].click();
    await settle();

    expect(api.markRead).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/tasks/KAN-3');
  });

  it('marks all of them read, with the menu still open, then reads count and list again', async () => {
    const element = await bell();
    await openMenu();
    const markAll = panel().querySelector<HTMLButtonElement>('.mark-all')!;
    expect(markAll.textContent.trim()).toBe('Marchează toate ca citite');
    unread = 0;
    items = items.map((item) => ({ ...item, readAt: new Date().toISOString() }));

    markAll.click();
    await settle();

    expect(api.markAllRead).toHaveBeenCalledTimes(1);
    expect(document.querySelector('.kb-notifications-menu')).not.toBeNull();
    expect(rows().map((row) => row.classList.contains('unread'))).toEqual([false, false]);
    expect(badge(element)).toBeNull();
    expect(markAll.disabled).toBe(true);
  });

  it('leads to every notification', async () => {
    await bell();
    await openMenu();

    const all = panel().querySelector<HTMLAnchorElement>('a.all')!;

    expect(all.textContent.trim()).toBe('Vezi toate');
    expect(all.getAttribute('href')).toBe('/notifications');
  });

  it('forgets the count and the list when the session ends, and stops listening', async () => {
    const element = await bell();
    await openMenu();

    signedIn.set(false);
    TestBed.tick();
    await settle();

    expect(badge(element)).toBeNull();
    expect(TestBed.inject(BellStore).items()).toEqual([]);
    expect(realtime.listening('NotificationCreated')).toBe(0);
  });

  it('starts again for the next session', async () => {
    await bell();
    signedIn.set(false);
    TestBed.tick();

    unread = 7;
    signedIn.set(true);
    TestBed.tick();
    await vi.waitFor(() => {
      expect(realtime.opened).toBe(2);
    });
    await fixture.whenStable();

    expect(badge(fixture.nativeElement as HTMLElement)).toBe('7');
    expect(realtime.listening('NotificationCreated')).toBe(1);
  });

  it('reads nothing while the password has to change, as the API would refuse it', async () => {
    mustChangePassword.set(true);
    fixture = TestBed.createComponent(NotificationBell);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(api.unreadCount).not.toHaveBeenCalled();
    expect(realtime.opened).toBe(0);
  });
});
