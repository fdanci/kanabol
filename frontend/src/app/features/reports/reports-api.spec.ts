import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { QUIET } from '../../core/api/error-interceptor';
import { ReportFilters, apiInterval } from './report-filters';
import {
  aCompletedReport,
  aCycleTimeReport,
  aDeadlinesReport,
  aWorkTimeReport,
} from './reports-testing';
import { ReportsApi } from './reports-api';

const OCTOBER: ReportFilters = {
  from: '2026-10-01',
  to: '2026-10-31',
  project: null,
  user: null,
  granularity: 'week',
};

/**
 * docs/SPEC.md §5.1.1: the three reports take from and to as instants with the person's offset, and
 * projectId, userId and granularity only when there are; the project select reads every project the person
 * sees, archived too, and the user select a project's members, quietly.
 */
describe('ReportsApi', () => {
  let api: ReportsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ReportsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the finished tasks of the interval, by the week, for every project', async () => {
    const reading = firstValueFrom(api.completed(OCTOBER));

    const request = http.expectOne((sent) => sent.url === '/api/reports/completed');
    expect(request.request.params.keys()).toEqual(['from', 'to', 'granularity']);
    expect(request.request.params.get('from')).toBe(apiInterval(OCTOBER).from);
    expect(request.request.params.get('to')).toBe(apiInterval(OCTOBER).to);
    expect(request.request.params.get('granularity')).toBe('week');
    request.flush(aCompletedReport());

    expect(await reading).toEqual(aCompletedReport());
  });

  // A + left as it is reaches the API as a space, and the instant would not be one.
  it('sends the offset with its + encoded', () => {
    void firstValueFrom(api.completed(OCTOBER));

    const request = http.expectOne((sent) => sent.url === '/api/reports/completed');
    expect(request.request.urlWithParams).not.toMatch(/T00:00:00\+/);
    expect(request.request.urlWithParams).toMatch(/from=2026-10-01T00%3A00%3A00(%2B|-)\d\d%3A\d\d/);
    request.flush(aCompletedReport());
  });

  it('sends the project, the person and the month when chosen', () => {
    void firstValueFrom(
      api.deadlines({ ...OCTOBER, project: 'p1', user: 'u2', granularity: 'month' }),
    );

    const request = http.expectOne((sent) => sent.url === '/api/reports/deadlines');
    expect(request.request.params.get('projectId')).toBe('p1');
    expect(request.request.params.get('userId')).toBe('u2');
    expect(request.request.params.get('granularity')).toBe('month');
    request.flush(aDeadlinesReport());
  });

  it('reads the times without a granularity', () => {
    void firstValueFrom(api.cycleTime({ ...OCTOBER, project: 'p1' }));

    const request = http.expectOne((sent) => sent.url === '/api/reports/cycle-time');
    expect(request.request.params.keys()).toEqual(['from', 'to', 'projectId']);
    request.flush(aCycleTimeReport());
  });

  // Group 22: the estimate against the worked time, as cycle-time without a granularity, and the schedule.
  it('reads the work time without a granularity, and the work schedule', () => {
    void firstValueFrom(api.workTime({ ...OCTOBER, user: 'u2' }));
    void firstValueFrom(api.workSchedule());

    const request = http.expectOne((sent) => sent.url === '/api/reports/work-time');
    expect(request.request.params.keys()).toEqual(['from', 'to', 'userId']);
    request.flush(aWorkTimeReport());
    http
      .expectOne('/api/settings/work-schedule')
      .flush({ days: [1, 2, 3, 4, 5], start: '08:30', end: '16:30' });
  });

  it('reads every project the person sees, archived ones too', () => {
    void firstValueFrom(api.projects());

    const request = http.expectOne((sent) => sent.url === '/api/projects');
    expect(request.request.params.get('includeArchived')).toBe('true');
    expect(request.request.params.get('pageSize')).toBe('100');
    request.flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
  });

  it("reads a project's members quietly: without them the select offers the report's people", () => {
    void firstValueFrom(api.members('p1'));

    const request = http.expectOne((sent) => sent.url === '/api/projects/p1/members');
    expect(request.request.context.get(QUIET)).toBe(true);
    request.flush([]);
  });
});
