import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { Injector } from '@angular/core';
import { Observable, of } from 'rxjs';
import { SidebarStore } from './sidebar-store';

/** A window this many pixels wide, for the breakpoints of docs/UI_UX_SPEC.md §10. */
function windowOf(width: number): SidebarStore {
  const matches = (query: string): boolean => {
    const min = /min-width: (\d+)/.exec(query);
    const max = /max-width: ([\d.]+)/.exec(query);
    return (!min || width >= Number(min[1])) && (!max || width <= Number(max[1]));
  };
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: matches(query) }));
  const breakpoints = {
    observe: (query: string): Observable<BreakpointState> =>
      of({ matches: matches(query), breakpoints: {} }),
  };
  return Injector.create({
    providers: [SidebarStore, { provide: BreakpointObserver, useValue: breakpoints }],
  }).get(SidebarStore);
}

describe('SidebarStore', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is open at 240px from 1280px up', () => {
    const sidebar = windowOf(1440);

    expect(sidebar.isDrawer()).toBe(false);
    expect(sidebar.opened()).toBe(true);
    expect(sidebar.collapsed()).toBe(false);
  });

  it('is collapsed to its icons between 960 and 1279px', () => {
    const sidebar = windowOf(1100);

    expect(sidebar.opened()).toBe(true);
    expect(sidebar.collapsed()).toBe(true);
  });

  it('remembers what the user chose', () => {
    windowOf(1100).toggle();

    expect(localStorage.getItem('kb.sidebar')).toBe('expanded');
    expect(windowOf(1100).collapsed()).toBe(false);

    windowOf(1440).toggle();
    expect(localStorage.getItem('kb.sidebar')).toBe('collapsed');
    expect(windowOf(1440).collapsed()).toBe(true);
  });

  it('is a drawer below 960px, closed until asked for, and always full width there', () => {
    localStorage.setItem('kb.sidebar', 'collapsed');
    const sidebar = windowOf(800);

    expect(sidebar.isDrawer()).toBe(true);
    expect(sidebar.opened()).toBe(false);
    expect(sidebar.collapsed()).toBe(false);

    sidebar.toggle();
    expect(sidebar.opened()).toBe(true);
    expect(localStorage.getItem('kb.sidebar')).toBe('collapsed');

    sidebar.closeDrawer();
    expect(sidebar.opened()).toBe(false);
  });

  it('ignores a value in storage it does not know', () => {
    localStorage.setItem('kb.sidebar', 'sideways');

    expect(windowOf(1440).collapsed()).toBe(false);
  });
});
