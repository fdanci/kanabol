import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSlideToggleHarness } from '@angular/material/slide-toggle/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthStore } from '../auth/auth-store';
import { BellStore } from '../notifications/bell-store';
import { CurrentUserDto } from '../auth/models';
import { Lockup } from '../../shared/ui/lockup';
import { PageHeader } from '../../shared/ui/page-header';
import { Navbar } from './navbar';
import { Shell } from './shell';
import { Sidenav } from './sidenav';
import { ThemeStore } from './theme-store';

const ANA: CurrentUserDto = {
  id: '1',
  email: 'ana.pop@example.com',
  displayName: 'Ana Pop',
  roleName: 'Manager',
  mustChangePassword: false,
  permissions: [],
};

class FakeAuth {
  readonly held = signal<string[]>([]);
  readonly user = signal<CurrentUserDto | null>(ANA);
  has = (permission: string) => this.held().includes(permission);
  logout = vi.fn(() => Promise.resolve());
}

class FakeTheme {
  readonly isDark = signal(false);
  toggle = vi.fn(() => {
    this.isDark.update((dark) => !dark);
  });
}

/** matchMedia for a wide window, with what the CDK's BreakpointObserver calls on it. */
function wideWindow(query: string) {
  return {
    matches: !query.includes('max-width'),
    media: query,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  };
}

describe('layout', () => {
  let auth: FakeAuth;
  let theme: FakeTheme;

  beforeEach(() => {
    auth = new FakeAuth();
    theme = new FakeTheme();
    vi.stubGlobal('matchMedia', wideWindow);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: AuthStore, useValue: auth },
        { provide: ThemeStore, useValue: theme },
        {
          provide: BellStore,
          useValue: {
            count: signal(4),
            items: signal([]),
            state: signal('idle'),
            opened: vi.fn(),
            closed: vi.fn(),
          },
        },
      ],
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('Sidenav', () => {
    async function sidenav(collapsed = false): Promise<HTMLElement> {
      const fixture = TestBed.createComponent(Sidenav);
      fixture.componentRef.setInput('collapsed', collapsed);
      await fixture.whenStable();
      return fixture.nativeElement as HTMLElement;
    }

    function labels(element: HTMLElement): string[] {
      return Array.from(element.querySelectorAll('a .label'), (label) => label.textContent.trim());
    }

    it('lists the work pages for everyone', async () => {
      const element = await sidenav();

      expect(labels(element)).toEqual([
        'Panou principal',
        'Proiectele mele',
        'Task-urile mele',
        'Notificări',
      ]);
      expect(Array.from(element.querySelectorAll('h2'), (title) => title.textContent)).toEqual([
        'Lucru',
      ]);
    });

    it('adds only the administration pages the user has the permission for', async () => {
      auth.held.set(['users.manage', 'reports.view']);

      const element = await sidenav();

      expect(labels(element)).toEqual([
        'Panou principal',
        'Proiectele mele',
        'Task-urile mele',
        'Notificări',
        'Utilizatori',
        'Rapoarte',
      ]);
      expect(element.querySelectorAll('h2')[1].textContent).toBe('Administrare');
    });

    // docs/UI_UX_SPEC.md §5 (group 22): "Setări generale" last in Administrare, for settings.manage, /settings.
    it('adds Setări generale last in Administrare for settings.manage', async () => {
      auth.held.set(['reports.view', 'settings.manage']);

      const element = await sidenav();

      expect(labels(element).slice(-2)).toEqual(['Rapoarte', 'Setări generale']);
      const link = [...element.querySelectorAll('a')].find((found) =>
        found.textContent.includes('Setări generale'),
      )!;
      expect(link.getAttribute('href')).toBe('/settings');
      expect(link.querySelector('.material-symbols-outlined')?.textContent.trim()).toBe('settings');
    });

    it('marks the page the user is on', async () => {
      await TestBed.inject(Router).navigateByUrl('/projects');

      const element = await sidenav();

      const current = element.querySelector('a[aria-current=page]')!;
      expect(current.textContent).toContain('Proiectele mele');
      expect(current.classList).toContain('active');
      expect(element.querySelectorAll('a.active')).toHaveLength(1);
    });

    it('keeps only the icons when collapsed, with the names still there for screen readers', async () => {
      const element = await sidenav(true);

      expect(element.classList).toContain('collapsed');
      expect(element.querySelector('h2')?.classList).toContain('kb-visually-hidden');
      expect(labels(element)[0]).toBe('Panou principal');
    });

    it('shows the version at its foot', async () => {
      expect((await sidenav()).querySelector('.version')?.textContent.trim()).toMatch(
        /^Versiunea \d+\.\d+\.\d+$/,
      );
    });

    // Every part of the page in a landmark, as axe-core asks (group 19b): the version is the page's footer.
    it('puts the version in a footer, the links in the navigation', async () => {
      const element = await sidenav();

      expect(element.querySelector('.version')?.tagName).toBe('FOOTER');
      expect(element.querySelector('nav')?.getAttribute('aria-label')).toBe('Navigare');
    });
  });

  describe('Navbar', () => {
    async function navbar() {
      const fixture = TestBed.createComponent(Navbar);
      await fixture.whenStable();
      return {
        fixture,
        element: fixture.nativeElement as HTMLElement,
        loader: TestbedHarnessEnvironment.loader(fixture),
      };
    }

    // Every part of the page in a landmark, as axe-core asks (group 19b): the toolbar is the banner.
    it('is the banner of the page', async () => {
      const { element } = await navbar();

      expect(element.getAttribute('role')).toBe('banner');
    });

    it('links the lockup to the start page, with a name a screen reader can say', async () => {
      const { element } = await navbar();

      const home = element.querySelector('a.home')!;
      expect(home.getAttribute('href')).toBe('/');
      expect(home.getAttribute('aria-label')).toBe('Kanabol — Panou principal');
      expect(home.querySelector('kb-lockup')).not.toBeNull();
    });

    // docs/UI_UX_SPEC.md §7.6: the bell opens its menu, with the unread count on it (group 17).
    it('holds the bell, with its count, before the user menu', async () => {
      const { element } = await navbar();

      const bell = element.querySelector('kb-notification-bell')!;
      expect(bell).not.toBeNull();
      expect(bell.querySelector('.badge')?.textContent.trim()).toBe('4');
      expect(bell.querySelector('button')?.getAttribute('aria-haspopup')).toBe('menu');
      expect(bell.nextElementSibling?.classList).toContain('user');
    });

    // Global search was taken out of v1 (docs/UI_UX_SPEC.md v1.5).
    it('has no search', async () => {
      const { element } = await navbar();

      expect(element.querySelector('input')).toBeNull();
    });

    it('asks for the sidebar, and says whether it is open', async () => {
      const { fixture, element } = await navbar();
      let toggled = 0;
      fixture.componentInstance.sidebarToggled.subscribe(() => toggled++);
      fixture.componentRef.setInput('sidebarOpen', false);
      await fixture.whenStable();

      const button = element.querySelector<HTMLButtonElement>('button[aria-controls=kb-sidebar]')!;
      expect(button.getAttribute('aria-expanded')).toBe('false');
      button.click();

      expect(toggled).toBe(1);
    });

    it('shows who is signed in, with the theme and the way out in their menu', async () => {
      const { fixture, element, loader } = await navbar();
      expect(element.querySelector('.user')?.textContent).toContain('ana.pop@example.com');
      expect(element.querySelector('.user kb-avatar')?.textContent).toContain('AP');

      // The theme's switch comes with @defer (on idle), before anyone opens the menu.
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      await fixture.whenStable();

      const menu = await loader.getHarness(MatMenuHarness.with({ selector: '.user' }));
      await menu.open();
      const toggle = await (await menu.getChildLoader('.theme')).getHarness(MatSlideToggleHarness);
      expect(await toggle.getLabelText()).toBe('Temă întunecată');
      await toggle.toggle();
      expect(theme.toggle).toHaveBeenCalled();

      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await menu.clickItem({ text: /Ieși din cont/ });
      await Promise.resolve();
      expect(auth.logout).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith('/login');
    });
  });

  describe('Shell', () => {
    it('puts the skip link first, and it takes the focus to the page', async () => {
      const fixture = TestBed.createComponent(Shell);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      document.body.appendChild(element);

      const skip = element.firstElementChild as HTMLAnchorElement;
      expect(skip.textContent).toBe('Sari la conținut');
      skip.click();

      expect(document.activeElement?.id).toBe('main');
      expect(element.querySelector('kb-navbar')).not.toBeNull();
      expect(element.querySelector('kb-sidenav')).not.toBeNull();
      element.remove();
    });
  });

  describe('PageHeader', () => {
    it('shows the title and the way to it, the last step being the page itself', async () => {
      const fixture = TestBed.createComponent(PageHeader);
      fixture.componentRef.setInput('title', 'Kanabol v1');
      fixture.componentRef.setInput('crumbs', [
        { label: 'Acasă', link: '/' },
        { label: 'Proiecte', link: '/projects' },
        { label: 'Kanabol v1' },
      ]);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;

      expect(element.querySelector('h1')?.textContent).toBe('Kanabol v1');
      expect(Array.from(element.querySelectorAll('nav a'), (link) => link.textContent)).toEqual([
        'Acasă',
        'Proiecte',
      ]);
      expect(element.querySelector('[aria-current=page]')?.textContent).toBe('Kanabol v1');
    });
  });

  describe('Lockup', () => {
    it('draws the mark inline, hidden from screen readers, next to the name', async () => {
      const fixture = TestBed.createComponent(Lockup);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;

      const mark = element.querySelector('svg')!;
      expect(mark.getAttribute('viewBox')).toBe('8 28 84 44');
      expect(mark.getAttribute('aria-hidden')).toBe('true');
      expect(mark.querySelectorAll('rect')).toHaveLength(9);
      expect(element.querySelector('.word')?.textContent).toBe('Kanabol');
      expect(element.querySelector('img')).toBeNull();
    });
  });

  describe('routes', () => {
    beforeEach(async () => {
      const { routes } = await import('../../app.routes');
      TestBed.resetTestingModule();
      vi.stubGlobal('matchMedia', wideWindow);
      TestBed.configureTestingModule({
        providers: [
          provideRouter(routes),
          { provide: AuthStore, useValue: auth },
          { provide: ThemeStore, useValue: theme },
        ],
      });
      Object.assign(auth, { isSignedIn: () => true, mustChangePassword: () => false });
    });

    it('opens a page inside the shell', async () => {
      const harness = await RouterTestingHarness.create('/my-tasks');

      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Task-urile mele');
      expect(document.title).toBe('Task-urile mele · Kanabol');
    });

    // At the address typed, without the page (docs/UI_UX_SPEC.md §6.8).
    it('shows the 403 page for a page without its permission', async () => {
      const harness = await RouterTestingHarness.create('/users');

      expect(harness.routeNativeElement?.textContent).toContain('Nu ai acces la această pagină');
      expect(TestBed.inject(Location).path()).toBe('/users');
    });

    it('opens it with the permission', async () => {
      auth.held.set(['users.manage']);

      const harness = await RouterTestingHarness.create('/users');

      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Utilizatori');
    });
  });
});
