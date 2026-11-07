import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { TaskChanges } from '../../shared/task-changes';
import { CommentDto } from './models';
import { CommentsApi } from './comments-api';
import { TaskCommentsStore } from './task-comments-store';
import { aComment, refused } from './task-testing';

/** docs/UI_UX_SPEC.md §7.5: a task's comments, from the oldest, added at the end and edited in place. */
describe('TaskCommentsStore', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let changes: TaskChanges;
  let store: TaskCommentsStore;

  beforeEach(() => {
    api = {
      list: vi.fn(() => of([aComment({ id: 'cm1', body: 'Primul' })])),
      add: vi.fn((_taskId: string, body: string) => of(aComment({ id: 'cm2', body }))),
      edit: vi.fn((id: string, body: string) =>
        of(aComment({ id, body, updatedAt: '2026-11-07T10:00:00Z' })),
      ),
    };
    TestBed.configureTestingModule({
      providers: [TaskCommentsStore, { provide: CommentsApi, useValue: api }],
    });
    store = TestBed.inject(TaskCommentsStore);
    changes = TestBed.inject(TaskChanges);
  });

  it('reads the comments of the task it is given', async () => {
    await store.load('t1');

    expect(api['list']).toHaveBeenCalledWith('t1');
    expect(store.state()).toBe('ready');
    expect(store.comments().map((comment) => comment.body)).toEqual(['Primul']);
  });

  it('keeps only the comments of the last task asked for, however the answers arrive', async () => {
    const first = new Subject<CommentDto[]>();
    api['list']
      .mockReturnValueOnce(first)
      .mockReturnValueOnce(of([aComment({ body: 'Al doilea' })]));

    const loadingFirst = store.load('t1');
    await store.load('t2');
    first.next([aComment({ body: 'Al primului' })]);
    first.complete();
    await loadingFirst;

    expect(store.comments().map((comment) => comment.body)).toEqual(['Al doilea']);
  });

  it('says when the comments could not be read', async () => {
    api['list'].mockReturnValueOnce(refused(500, 'Server'));

    await store.load('t1');

    expect(store.state()).toBe('error');
  });

  it('adds a comment at the end, and tells the board its count changed', async () => {
    await store.load('t1');
    const before = changes.version();

    expect(await store.add('Al doilea')).toBe(true);

    expect(api['add']).toHaveBeenCalledWith('t1', 'Al doilea');
    expect(store.comments().map((comment) => comment.body)).toEqual(['Primul', 'Al doilea']);
    expect(changes.version()).toBe(before + 1);
  });

  it('keeps the list as it was when a comment is refused', async () => {
    await store.load('t1');
    api['add'].mockReturnValueOnce(refused(409, 'Task.Archived'));

    expect(await store.add('Prea tarziu')).toBe(false);

    expect(store.comments()).toHaveLength(1);
  });

  it('puts an edited comment in its place', async () => {
    api['list'].mockReturnValueOnce(
      of([aComment({ id: 'cm1', body: 'Unu' }), aComment({ id: 'cm2', body: 'Doi' })]),
    );
    await store.load('t1');

    expect(await store.edit('cm1', 'Unu, schimbat')).toBe(true);

    expect(api['edit']).toHaveBeenCalledWith('cm1', 'Unu, schimbat');
    expect(store.comments().map((comment) => comment.body)).toEqual(['Unu, schimbat', 'Doi']);
    expect(store.comments()[0].updatedAt).toBe('2026-11-07T10:00:00Z');
  });

  it('keeps a comment as it was when its edit is refused', async () => {
    await store.load('t1');
    api['edit'].mockReturnValueOnce(refused(403, 'Comment.NotYours'));

    expect(await store.edit('cm1', 'Altfel')).toBe(false);

    expect(store.comments()[0].body).toBe('Primul');
  });
});
