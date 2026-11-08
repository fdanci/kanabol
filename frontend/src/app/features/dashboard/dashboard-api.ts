import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { Page } from '../../shared/models/page';
import { DashboardProjectDto, DashboardTaskDto, MySummaryDto } from './models';

/** The API's largest page: "Proiectele mele" shows them all (docs/SPEC.md §7.3). */
const MOST_PROJECTS = 100;

/**
 * What the main panel reads (docs/SPEC.md §4.4, "Panoul principal și task-urile mele"), each on its own,
 * without a common endpoint: the four numbers, the first open tasks and the projects the user is a member
 * of. The notifications come through NotificationsApi.
 */
@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly api = inject(ApiClient);

  /** weekStart is the Monday at midnight where the person is, as an instant. */
  summary(weekStart: string): Observable<MySummaryDto> {
    return this.api.get<MySummaryDto>('/me/summary', {
      params: new HttpParams().set('weekStart', weekStart),
    });
  }

  /** The user's own open tasks, by deadline from the soonest, those without one last. */
  openTasks(count: number): Observable<Page<DashboardTaskDto>> {
    return this.api.get<Page<DashboardTaskDto>>('/me/tasks', {
      params: new HttpParams().set('open', true).set('pageSize', count),
    });
  }

  /** The projects the user is a member of, by name, not those projects.view_all shows. */
  myProjects(): Observable<Page<DashboardProjectDto>> {
    return this.api.get<Page<DashboardProjectDto>>('/projects', {
      params: new HttpParams().set('mine', true).set('pageSize', MOST_PROJECTS),
    });
  }
}
