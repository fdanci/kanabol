import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HANDLED_CODES } from '../../core/api/error-interceptor';
import { anEntry } from './task-testing';
import { TasksApi } from './tasks-api';

/**
 * docs/SPEC.md §4.4 "Istoricul unui task": a Page from the newest, 50 at a time, and one entry whole,
 * with a description's texts.
 */
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

  // docs/SPEC.md §4.4 "Panoul principal și task-urile mele": the project list's filters but the assignee.
  it('reads a page of the user’s own tasks, with only the filters asked for', async () => {
    const reading = firstValueFrom(
      api.myTasks({
        page: 1,
        pageSize: 50,
        sort: { field: 'dueDate', descending: false },
        open: true,
        priorities: ['High', 'Critical'],
        overdue: true,
        search: ' login ',
        includeArchived: true,
      }),
    );

    const request = http.expectOne((sent) => sent.url === '/api/me/tasks' && sent.method === 'GET');
    expect(request.request.params.toString()).toBe(
      'page=1&pageSize=50&open=true&priority=High&priority=Critical&overdue=true&search=login&includeArchived=true',
    );
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });

    expect((await reading).totalCount).toBe(0);
  });

  it('sends the sort only when it is not by deadline, and no open for the finished ones too', () => {
    void firstValueFrom(
      api.myTasks({
        page: 2,
        pageSize: 25,
        sort: { field: 'key', descending: true },
        open: false,
        priorities: [],
        overdue: false,
        search: '',
        includeArchived: false,
      }),
    );

    const request = http.expectOne((sent) => sent.url === '/api/me/tasks');
    expect(request.request.params.toString()).toBe('page=2&pageSize=25&sort=key:desc');
    request.flush({ items: [], page: 2, pageSize: 25, totalCount: 0 });
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

  it('reads one entry of a task’s history, with its texts, saying itself that it is not there', async () => {
    const entry = anEntry({ id: 'h7', field: 'Description', oldValue: 'Veche', newValue: 'Nouă' });
    const reading = firstValueFrom(api.historyEntry('t1', 'h7'));

    const request = http.expectOne(
      (sent) => sent.url === '/api/tasks/t1/history/h7' && sent.method === 'GET',
    );
    expect(request.request.params.keys()).toEqual([]);
    expect(request.request.context.get(HANDLED_CODES)).toEqual(['History.NotFound']);
    request.flush(entry);

    expect(await reading).toEqual(entry);
  });
});
