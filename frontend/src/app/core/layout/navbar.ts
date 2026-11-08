import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatDivider } from '@angular/material/divider';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltip } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { NotificationBell } from '../notifications/notification-bell';
import { Avatar } from '../../shared/ui/avatar';
import { Lockup } from '../../shared/ui/lockup';
import { ThemeToggle } from '../../shared/ui/theme-toggle';
import { ThemeStore } from './theme-store';

/**
 * The toolbar (docs/UI_UX_SPEC.md §5, §6.0.8): 52px, translucent, a line under it and no shadow. The
 * sidebar button and the lockup on the left, the bell and the user's menu on the right. There's no
 * global search in v1.
 */
@Component({
  selector: 'kb-navbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatIconButton,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatDivider,
    MatIcon,
    MatTooltip,
    RouterLink,
    Avatar,
    Lockup,
    ThemeToggle,
    NotificationBell,
  ],
  template: `
    <button
      matIconButton
      type="button"
      class="tool"
      [attr.aria-expanded]="sidebarOpen()"
      aria-controls="kb-sidebar"
      i18n-aria-label
      aria-label="Bara laterală"
      i18n-matTooltip
      matTooltip="Bara laterală"
      (click)="sidebarToggled.emit()"
    >
      <mat-icon aria-hidden="true">dock_to_left</mat-icon>
    </button>
    <a class="home" routerLink="/" i18n-aria-label aria-label="Kanabol — Panou principal"
      ><kb-lockup
    /></a>

    <span class="spacer"></span>

    <kb-notification-bell />

    @if (auth.user(); as user) {
      <button
        type="button"
        class="user"
        [matMenuTriggerFor]="userMenu"
        i18n-aria-label
        aria-label="Contul tău: {{ user.email }}"
      >
        <kb-avatar [name]="user.displayName" />
        <span class="email kb-truncate">{{ user.email }}</span>
        <span class="material-symbols-outlined chevron" aria-hidden="true">expand_more</span>
      </button>
      <mat-menu #userMenu="matMenu" class="kb-menu" xPosition="before">
        <div class="who" role="presentation">
          <span class="name">{{ user.displayName }}</span>
          <span class="kb-caption">{{ user.roleName }}</span>
        </div>
        <mat-divider />
        <div
          class="theme"
          role="presentation"
          (click)="$event.stopPropagation()"
          (keydown)="$event.stopPropagation()"
        >
          <kb-theme-toggle [dark]="theme.isDark()" (toggled)="theme.toggle()" i18n
            >Temă întunecată</kb-theme-toggle
          >
        </div>
        <mat-divider />
        <button mat-menu-item type="button" (click)="signOut()">
          <mat-icon aria-hidden="true">logout</mat-icon>
          <span i18n>Ieși din cont</span>
        </button>
      </mat-menu>
    }
  `,
  styleUrl: './navbar.scss',
})
export class Navbar {
  readonly sidebarOpen = input(true);
  readonly sidebarToggled = output();

  protected readonly auth = inject(AuthStore);
  protected readonly theme = inject(ThemeStore);
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
