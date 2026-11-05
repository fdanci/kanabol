import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatSidenav, MatSidenavContainer, MatSidenavContent } from '@angular/material/sidenav';
import { RouterOutlet } from '@angular/router';
import { Navbar } from './navbar';
import { Sidenav } from './sidenav';
import { SidebarStore } from './sidebar-store';

/**
 * Every signed-in page (docs/UI_UX_SPEC.md §5): the toolbar on top, the source list on the left and
 * the page in the middle, at most 1440px wide. A skip link comes first, for keyboards (§9).
 */
@Component({
  selector: 'kb-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatSidenavContainer, MatSidenav, MatSidenavContent, RouterOutlet, Navbar, Sidenav],
  template: `
    <a class="skip" href="#main" (click)="skipToContent($event)" i18n>Sari la conținut</a>
    <kb-navbar
      [sidebarOpen]="sidebar.opened() && !sidebar.collapsed()"
      (sidebarToggled)="sidebar.toggle()"
    />
    <mat-sidenav-container class="body" autosize [hasBackdrop]="sidebar.isDrawer()">
      <mat-sidenav
        id="kb-sidebar"
        class="sidebar kb-glass"
        [mode]="sidebar.isDrawer() ? 'over' : 'side'"
        [opened]="sidebar.opened()"
        (closedStart)="sidebar.closeDrawer()"
      >
        <kb-sidenav [collapsed]="sidebar.collapsed()" (navigated)="sidebar.closeDrawer()" />
      </mat-sidenav>
      <mat-sidenav-content>
        <main id="main" tabindex="-1">
          <router-outlet />
        </main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .skip {
      position: absolute;
      top: var(--kb-space-2);
      left: var(--kb-space-2);
      z-index: 10;
      padding: var(--kb-space-2) var(--kb-space-3);
      border-radius: var(--kb-radius-sm);
      background: var(--kb-surface-0);
      box-shadow: var(--kb-shadow-popover);
      transform: translateY(-200%);
    }

    .skip:focus-visible {
      transform: none;
    }

    .body {
      flex: 1;
      background: var(--kb-surface-50);
    }

    main {
      box-sizing: border-box;
      max-width: var(--kb-content-max);
      margin: 0 auto;
      padding: 0 0 var(--kb-space-6);
      outline: none;
    }
  `,
})
export class Shell {
  protected readonly sidebar = inject(SidebarStore);

  protected skipToContent(event: Event): void {
    event.preventDefault();
    document.getElementById('main')?.focus();
  }
}
