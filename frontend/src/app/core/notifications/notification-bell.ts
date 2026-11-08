import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatDivider } from '@angular/material/divider';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatTooltip } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Skeleton } from '../../shared/ui/skeleton';
import { BellStore } from './bell-store';
import { NotificationDto } from './models';
import { NotificationLine } from './notification-line';
import { badgeText } from './notification-text';

/**
 * The bell in the navbar (docs/UI_UX_SPEC.md §7.6): the unread count as a pill badge, none at 0 and
 * "99+" over 99, and a 400px menu with "Marchează toate ca citite", the last 10 and "Vezi toate". A row
 * marks its notification read and opens its task. The menu's items are mat-menu-items, so the arrows
 * reach every one of them; the header's button keeps the menu open.
 */
@Component({
  selector: 'kb-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatDivider,
    MatTooltip,
    RouterLink,
    Skeleton,
    NotificationLine,
  ],
  template: `
    <button
      matIconButton
      type="button"
      class="tool"
      [matMenuTriggerFor]="menu"
      [attr.aria-label]="label()"
      i18n-matTooltip
      matTooltip="Notificări"
      (menuOpened)="store.opened()"
      (menuClosed)="store.closed()"
    >
      <mat-icon aria-hidden="true">notifications</mat-icon>
    </button>
    @if (badge(); as badge) {
      <span class="badge kb-badge" aria-hidden="true">{{ badge }}</span>
    }

    <mat-menu #menu="matMenu" class="kb-menu kb-notifications-menu" xPosition="before">
      <div class="head">
        <span class="title" i18n>Notificări</span>
        <button
          mat-menu-item
          type="button"
          class="mark-all"
          [disabled]="store.count() === 0"
          (click)="$event.stopPropagation(); store.markAllRead()"
          i18n
        >
          Marchează toate ca citite
        </button>
      </div>
      @switch (store.state()) {
        @case ('error') {
          <p class="note" role="status" i18n>Nu am putut încărca notificările.</p>
          <button
            mat-menu-item
            type="button"
            (click)="$event.stopPropagation(); store.opened()"
            i18n
          >
            Reîncearcă
          </button>
        }
        @case ('ready') {
          @for (notification of store.items(); track notification.id) {
            <button
              mat-menu-item
              type="button"
              class="row"
              [class.unread]="notification.readAt === null"
              (click)="open(notification)"
            >
              <kb-notification-line [notification]="notification" />
            </button>
          } @empty {
            <p class="note" i18n>Nicio notificare.</p>
          }
        }
        @default {
          <div class="loading" aria-busy="true">
            @for (row of [1, 2, 3]; track row) {
              <kb-skeleton width="85%" />
            }
          </div>
        }
      }
      <mat-divider />
      <a mat-menu-item routerLink="/notifications" class="all" i18n>Vezi toate</a>
    </mat-menu>
  `,
  styles: `
    :host {
      position: relative;
      display: inline-flex;
    }

    /* On the icon's top right corner; its look is kb-badge's (6.0.11). */
    .badge {
      position: absolute;
      top: 1px;
      right: 0;
      pointer-events: none;
    }

    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--kb-space-2);
      padding-left: 12px;
    }

    .title {
      font-size: 13px;
      font-weight: 600;
      color: var(--kb-text-900);
    }

    .mark-all {
      width: auto;
      flex: none;
    }

    .row.unread {
      background: var(--kb-tint-100);
    }

    .note {
      margin: 0;
      padding: 12px;
      font-size: 13px;
      color: var(--kb-text-600);
    }

    .loading {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 12px;
    }
  `,
})
export class NotificationBell {
  protected readonly store = inject(BellStore);
  private readonly router = inject(Router);

  protected readonly badge = computed(() => badgeText(this.store.count()));
  protected readonly label = computed(() => {
    const count = this.store.count();
    if (count === 0) {
      return $localize`Notificări`;
    }
    return count === 1
      ? $localize`Notificări, una necitită`
      : $localize`Notificări, ${count}:count: necitite`;
  });

  protected open(notification: NotificationDto): void {
    void this.store.markRead(notification);
    if (notification.task) {
      void this.router.navigateByUrl(`/tasks/${encodeURIComponent(notification.task.key)}`);
    }
  }
}
