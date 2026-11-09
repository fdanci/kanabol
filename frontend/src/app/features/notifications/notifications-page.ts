import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatOption } from '@angular/material/core';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatSelect } from '@angular/material/select';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BellStore } from '../../core/notifications/bell-store';
import {
  NOTIFICATION_TYPES,
  NotificationDto,
  NotificationQuery,
  NotificationType,
} from '../../core/notifications/models';
import { NotificationLine } from '../../core/notifications/notification-line';
import { notificationTypeLabel } from '../../core/notifications/notification-text';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { EmptyState } from '../../shared/ui/empty-state';
import { Skeleton } from '../../shared/ui/skeleton';
import { NotificationsStore } from './notifications-store';
import { providePaginatorLabels } from '../../shared/ui/paginator-labels';

const PAGE_SIZES = [25, 50, 100];
const DEFAULT_PAGE_SIZE = 50;

/** "Toate", for every type: mat-select takes null for no choice at all and shows nothing. */
const EVERY_TYPE = '';

/**
 * /notifications (docs/UI_UX_SPEC.md §7.6): the bell's rows, 50 to a page with the List view's paginator,
 * "Doar necitite" (unread=1) and the type (type=) in the address, and "Marchează toate ca citite" at the
 * top. A row marks its notification read and opens its task.
 */
@Component({
  selector: 'kb-notifications-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Its own Romanian labels, so the paginator stays out of the first bundle (docs/SPEC.md §6.2).
  providers: [providePaginatorLabels(), NotificationsStore],
  imports: [
    MatButton,
    MatOption,
    MatPaginator,
    MatSelect,
    PageHeader,
    EmptyState,
    Skeleton,
    NotificationLine,
  ],
  templateUrl: './notifications-page.html',
  styleUrl: './notifications-page.scss',
})
export class NotificationsPage {
  protected readonly store = inject(NotificationsStore);
  protected readonly bell = inject(BellStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly heading = $localize`Notificări`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Notificări` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca notificările.`;
  protected readonly emptyTitle = $localize`Nicio notificare.`;
  protected readonly noMatchTitle = $localize`Nicio notificare pentru filtrele alese.`;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly everyType = EVERY_TYPE;
  protected readonly types = NOTIFICATION_TYPES.map((type) => ({
    value: type,
    label: notificationTypeLabel(type),
  }));

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: convertToParamMap({}),
  });

  protected readonly query = computed<NotificationQuery>(() => {
    const params = this.params();
    const type = params.get('type');
    const size = positive(params.get('size'), DEFAULT_PAGE_SIZE);
    return {
      page: positive(params.get('page'), 1),
      pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
      unread: params.get('unread') === '1',
      type: NOTIFICATION_TYPES.includes(type as NotificationType)
        ? (type as NotificationType)
        : null,
    };
  });
  protected readonly filtered = computed(() => this.query().unread || this.query().type !== null);

  constructor() {
    // Only the address starts a load: what load reads mustn't start another.
    effect(() => {
      const query = this.query();
      untracked(() => {
        void this.store.load(query);
      });
    });
  }

  protected toggleUnread(): void {
    void this.navigate({ unread: this.query().unread ? null : 1, page: null });
  }

  protected chooseType(type: NotificationType | typeof EVERY_TYPE): void {
    void this.navigate({ type: type === EVERY_TYPE ? null : type, page: null });
  }

  protected clearFilters(): void {
    void this.navigate({ unread: null, type: null, page: null });
  }

  protected paged(event: PageEvent): void {
    void this.navigate({
      page: event.pageIndex === 0 ? null : event.pageIndex + 1,
      size: event.pageSize === DEFAULT_PAGE_SIZE ? null : event.pageSize,
    });
  }

  protected open(notification: NotificationDto): void {
    void this.store.markRead(notification);
    if (notification.task) {
      void this.router.navigateByUrl(`/tasks/${encodeURIComponent(notification.task.key)}`);
    }
  }

  protected markAllRead(): void {
    void this.store.markAllRead();
  }

  private navigate(queryParams: Record<string, string | number | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }
}

function positive(value: string | null, fallback: number): number {
  const number = Number(value);
  return Number.isInteger(number) && number > 0 ? number : fallback;
}
