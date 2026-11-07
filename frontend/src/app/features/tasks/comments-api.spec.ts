import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { CommentsApi } from './comments-api';
import { aComment } from './task-testing';

/** docs/SPEC.md §4.4: a task's comments, all at once from the oldest, added and edited one at a time. */
describe('CommentsApi', () => {
  let api: CommentsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(CommentsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads every comment of a task', async () => {
    const reading = firstValueFrom(api.list('t1'));

    const request = http.expectOne('/api/tasks/t1/comments');
    expect(request.request.method).toBe('GET');
    request.flush([aComment()]);

    expect(await reading).toEqual([aComment()]);
  });

  it('adds one with its body, and edits one by its id', async () => {
    const adding = firstValueFrom(api.add('t1', 'Merge'));
    const add = http.expectOne('/api/tasks/t1/comments');
    expect(add.request.method).toBe('POST');
    expect(add.request.body).toEqual({ body: 'Merge' });
    add.flush(aComment({ body: 'Merge' }));
    expect((await adding).body).toBe('Merge');

    const editing = firstValueFrom(api.edit('cm1', 'Altfel'));
    const edit = http.expectOne('/api/comments/cm1');
    expect(edit.request.method).toBe('PUT');
    expect(edit.request.body).toEqual({ body: 'Altfel' });
    edit.flush(aComment({ body: 'Altfel' }));
    expect((await editing).body).toBe('Altfel');
  });
});
