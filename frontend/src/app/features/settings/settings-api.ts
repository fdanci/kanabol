import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';

/**
 * The work schedule (docs/SPEC.md §4.4, "Setările generale"): the work days as ISO numbers, 1 Monday to 7
 * Sunday, the hours as HH:mm, and the time zone of the server's configuration, which no page changes.
 */
export interface WorkScheduleDto {
  days: number[];
  start: string;
  end: string;
  timeZone: string;
}

export type WorkScheduleChange = Omit<WorkScheduleDto, 'timeZone'>;

/** Anyone signed in reads it; settings.manage changes it, which the API checks in the database. */
@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly api = inject(ApiClient);

  workSchedule(): Observable<WorkScheduleDto> {
    return this.api.get<WorkScheduleDto>('/settings/work-schedule');
  }

  saveWorkSchedule(schedule: WorkScheduleChange): Observable<WorkScheduleDto> {
    return this.api.put<WorkScheduleDto>('/settings/work-schedule', schedule);
  }
}
