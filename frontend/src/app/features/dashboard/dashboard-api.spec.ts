import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { DashboardApi } from './dashboard-api';
import { aDashboardProject, aDashboardTask, aSummary } from './dashboard-testing';

/**
 * docs/SPEC.md §4.4, "Panoul principal și task-urile mele": the panel reads me/summary, me/tasks with
 * open=true and pageSize=8, and projects with mine=true, each on its own, without a common endpoint.
 */
describe('DashboardApi', () => {
  let api: DashboardApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(DashboardApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads the summary from the week start it is given', async () => {
    const reading = firstValueFrom(api.summary('2026-11-01T22:00:00.000Z'));

    const request = http.expectOne((sent) => sent.url === '/api/me/summary');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.toString()).toBe('weekStart=2026-11-01T22:00:00.000Z');
    request.flush(aSummary());

    expect(await reading).toEqual(aSummary());
  });

  it('reads the first open tasks, as many as asked for', async () => {
    const reading = firstValueFrom(api.openTasks(8));

    const request = http.expectOne((sent) => sent.url === '/api/me/tasks');
    expect(request.request.params.toString()).toBe('open=true&pageSize=8');
    request.flush({ items: [aDashboardTask()], page: 1, pageSize: 8, totalCount: 1 });

    expect((await reading).items).toEqual([aDashboardTask()]);
  });

  it("reads the projects the user is a member of, the API's most to a page", async () => {
    const reading = firstValueFrom(api.myProjects());

    const request = http.expectOne((sent) => sent.url === '/api/projects');
    expect(request.request.params.toString()).toBe('mine=true&pageSize=100');
    request.flush({ items: [aDashboardProject()], page: 1, pageSize: 100, totalCount: 1 });

    expect((await reading).items).toEqual([aDashboardProject()]);
  });
});
