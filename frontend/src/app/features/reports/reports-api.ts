import { HttpContext, HttpParameterCodec, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { QUIET } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import {
  CompletedReportDto,
  CycleTimeReportDto,
  DeadlinesReportDto,
  ReportPersonDto,
  ReportProjectDto,
  ReportWorkScheduleDto,
  WorkTimeReportDto,
} from './models';
import { ReportFilters, apiInterval } from './report-filters';

/** The API's largest page: the project select offers them all (docs/SPEC.md §7.3). */
const MOST_PROJECTS = 100;

/**
 * Every character encoded: Angular's own codec leaves a + as it is, which the API reads as a space, and the
 * offset of 2026-11-01T00:00:00+02:00 would be lost.
 */
class ExactCodec implements HttpParameterCodec {
  encodeKey(key: string): string {
    return encodeURIComponent(key);
  }

  encodeValue(value: string): string {
    return encodeURIComponent(value);
  }

  decodeKey(key: string): string {
    return decodeURIComponent(key);
  }

  decodeValue(value: string): string {
    return decodeURIComponent(value);
  }
}

/**
 * The reports (docs/SPEC.md §5.1.1), each read on its own, and what the filters offer: the projects the
 * person sees, archived ones too, and the members of the one chosen.
 */
@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly api = inject(ApiClient);

  completed(filters: ReportFilters): Observable<CompletedReportDto> {
    return this.api.get<CompletedReportDto>('/reports/completed', {
      params: reportParams(filters, true),
    });
  }

  cycleTime(filters: ReportFilters): Observable<CycleTimeReportDto> {
    return this.api.get<CycleTimeReportDto>('/reports/cycle-time', {
      params: reportParams(filters, false),
    });
  }

  /** The estimate against the worked time (group 22). */
  workTime(filters: ReportFilters): Observable<WorkTimeReportDto> {
    return this.api.get<WorkTimeReportDto>('/reports/work-time', {
      params: reportParams(filters, false),
    });
  }

  /** The schedule the worked time counts in, which the section names. */
  workSchedule(): Observable<ReportWorkScheduleDto> {
    return this.api.get<ReportWorkScheduleDto>('/settings/work-schedule');
  }

  deadlines(filters: ReportFilters): Observable<DeadlinesReportDto> {
    return this.api.get<DeadlinesReportDto>('/reports/deadlines', {
      params: reportParams(filters, true),
    });
  }

  projects(): Observable<ReportProjectDto[]> {
    return this.api
      .get<Page<ReportProjectDto>>('/projects', {
        params: new HttpParams().set('includeArchived', true).set('pageSize', MOST_PROJECTS),
      })
      .pipe(map((page) => page.items));
  }

  /** Quiet: someone with reports.view may not see the project's members, and the select does without them. */
  members(projectId: string): Observable<ReportPersonDto[]> {
    return this.api.get<ReportPersonDto[]>(`/projects/${encodeURIComponent(projectId)}/members`, {
      context: new HttpContext().set(QUIET, true),
    });
  }
}

function reportParams(filters: ReportFilters, withGranularity: boolean): HttpParams {
  const interval = apiInterval(filters);
  let params = new HttpParams({ encoder: new ExactCodec() })
    .set('from', interval.from)
    .set('to', interval.to);
  if (filters.project) {
    params = params.set('projectId', filters.project);
  }
  if (filters.user) {
    params = params.set('userId', filters.user);
  }
  return withGranularity ? params.set('granularity', filters.granularity) : params;
}
