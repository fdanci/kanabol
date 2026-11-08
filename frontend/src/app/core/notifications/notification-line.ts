import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { NotificationDto } from './models';
import { notificationIcon, notificationText } from './notification-text';

/**
 * One notification, as the bell's menu and the notifications page show it (docs/UI_UX_SPEC.md §7.6): the
 * type's icon, the text, the relative time with the date in its tooltip, and an unread one's 8px dot, which
 * a hidden word also says. The row around it gives the tint and the click.
 */
@Component({
  selector: 'kb-notification-line',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatTooltip, RelativeTimePipe],
  template: `
    <span class="material-symbols-outlined icon" aria-hidden="true">{{ icon() }}</span>
    <span class="body">
      <span class="text">{{ text() }}</span>
      <span class="time kb-caption" [matTooltip]="date()">{{
        notification().createdAt | relativeTime
      }}</span>
    </span>
    @if (unread()) {
      <span class="dot" aria-hidden="true"></span>
      <span class="kb-visually-hidden" i18n>Necitită</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      min-width: 0;
      width: 100%;
      text-align: left;
    }

    .icon {
      flex: none;
      margin-top: 1px;
      font-size: 20px;
      color: var(--kb-text-600);
    }

    .body {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 2px;
      min-width: 0;
    }

    .text {
      font-size: 13px;
      line-height: 18px;
      color: var(--kb-text-900);
      white-space: normal;
      overflow-wrap: anywhere;
    }

    .time {
      align-self: flex-start;
      color: var(--kb-text-500);
    }

    .dot {
      flex: none;
      width: 8px;
      height: 8px;
      margin-top: 5px;
      border-radius: 50%;
      background: var(--kb-accent-600);
    }
  `,
})
export class NotificationLine {
  readonly notification = input.required<NotificationDto>();

  protected readonly icon = computed(() => notificationIcon(this.notification().type));
  protected readonly text = computed(() => notificationText(this.notification()));
  protected readonly date = computed(() => fullDate(this.notification().createdAt));
  protected readonly unread = computed(() => this.notification().readAt === null);
}
