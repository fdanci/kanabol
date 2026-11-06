import { BreakpointObserver } from '@angular/cdk/layout';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

const STORAGE_KEY = 'kb.sidebar';

type Saved = 'expanded' | 'collapsed';

function saved(): Saved | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'expanded' || value === 'collapsed' ? value : null;
  } catch {
    return null;
  }
}

/**
 * The sidebar (docs/UI_UX_SPEC.md §6.0.7, §10): 240px from 1280px up, 56px below that unless the user
 * opened it, and a drawer over the page below 960px. What the user chose is remembered in kb.sidebar.
 */
@Injectable({ providedIn: 'root' })
export class SidebarStore {
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly chosen = signal<Saved | null>(saved());
  private readonly drawerOpen = signal(false);

  private readonly wide = toSignal(
    this.breakpoints.observe('(min-width: 1280px)').pipe(map((state) => state.matches)),
    {
      initialValue: window.matchMedia('(min-width: 1280px)').matches,
    },
  );

  /** Below 960px the sidebar is a drawer over the page, closed until asked for. */
  readonly isDrawer = toSignal(
    this.breakpoints.observe('(max-width: 959.98px)').pipe(map((state) => state.matches)),
    {
      initialValue: window.matchMedia('(max-width: 959.98px)').matches,
    },
  );

  readonly collapsed = computed(() => {
    if (this.isDrawer()) {
      return false;
    }
    const chosen = this.chosen();
    return chosen ? chosen === 'collapsed' : !this.wide();
  });

  readonly opened = computed(() => !this.isDrawer() || this.drawerOpen());

  toggle(): void {
    if (this.isDrawer()) {
      this.drawerOpen.update((open) => !open);
      return;
    }
    const next: Saved = this.collapsed() ? 'expanded' : 'collapsed';
    this.chosen.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Private windows may refuse storage: the choice then lasts as long as the page.
    }
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }
}
