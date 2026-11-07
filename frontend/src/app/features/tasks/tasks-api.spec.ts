import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { anEntry } from './task-testing';
import { TasksApi } from './tasks-api';

/** docs/SPEC.md §4.4 "Istoricul unui task": a Page from the newest, 50 at a time. */
describe('TasksApi', () => {
  let api: TasksApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(TasksApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads a page of a task’s history, 50 at a time', async () => {
    const page = { items: [anEntry()], page: 2, pageSize: 50, totalCount: 51 };
    const reading = firstValueFrom(api.history('t1', 2));

    const request = http.expectOne(
      (sent) => sent.url === '/api/tasks/t1/history' && sent.method === 'GET',
    );
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush(page);

    expect(await reading).toEqual(page);
  });
});
