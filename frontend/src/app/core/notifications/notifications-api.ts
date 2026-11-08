import { HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../api/api-client';
import { QUIET } from '../api/error-interceptor';
import { Page } from '../../shared/models/page';
import { NotificationCountDto, NotificationDto, NotificationQuery } from './models';

/**
 * The signed-in user's own notifications (docs/SPEC.md §4.4), for the bell in the navbar and the
 * notifications page: in core, since the navbar can't import a feature. Nobody sees another's.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly api = inject(ApiClient);

  /** A page from the newest. The bell's menu says itself that it couldn't read: quiet. */
  list(query: NotificationQuery, quiet = false): Observable<Page<NotificationDto>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.unread) {
      params = params.set('unread', true);
    }
    if (query.type) {
      params = params.set('type', query.type);
    }
    return this.api.get<Page<NotificationDto>>('/notifications', {
      params,
      context: quiet ? new HttpContext().set(QUIET, true) : undefined,
    });
  }

  /** Read in the background, after each new notification: a failure keeps the badge as it was. */
  unreadCount(): Observable<NotificationCountDto> {
    return this.api.get<NotificationCountDto>('/notifications/unread-count', {
      context: new HttpContext().set(QUIET, true),
    });
  }

  /** The notification, read; a second time keeps the first time it was read. */
  markRead(id: string): Observable<NotificationDto> {
    return this.api.post<NotificationDto>(`/notifications/${encodeURIComponent(id)}/read`);
  }

  /** How many it marked now. */
  markAllRead(): Observable<NotificationCountDto> {
    return this.api.post<NotificationCountDto>('/notifications/read-all');
  }
}
