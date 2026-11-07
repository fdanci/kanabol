import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { TaskItemDto } from './models';
import { TaskChanges } from '../../shared/task-changes';
import { aMember, aProject, aTask, refused } from './task-testing';
import { TaskPanelStore } from './task-panel-store';
import { TasksApi } from './tasks-api';

describe('TaskPanelStore', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  // A signal, as in the real AuthStore, so what reads it follows a change.
  const held = signal<string[]>([]);
  let changes: TaskChanges;
  let store: TaskPanelStore;

  beforeEach(() => {
    held.set([]);
    api = {
      byKey: vi.fn(() => of(aTask())),
      project: vi.fn(() => of(aProject())),
      members: vi.fn(() =>
        of([
          aMember('me', 'Florin Danci'),
          aMember('u2', 'Ana Pop'),
          aMember('u3', 'Bob Ionescu', false),
        ]),
      ),
      rename: vi.fn((_id: string, title: string) => of(aTask({ title }))),
      describe: vi.fn((_id: string, description: string | null) => of(aTask({ description }))),
      assign: vi.fn((_id: string, assigneeIds: string[]) =>
        of(aTask({ assignees: assigneeIds.map((userId) => ({ userId, displayName: userId })) })),
      ),
      archive: vi.fn(() => of(undefined)),
      restore: vi.fn(() => of(aTask())),
    };
    TestBed.configureTestingModule({
      providers: [
        TaskPanelStore,
        { provide: TasksApi, useValue: api },
        {
          provide: AuthStore,
          useValue: { has: (permission: string) => held().includes(permission) },
        },
      ],
    });
    store = TestBed.inject(TaskPanelStore);
    changes = TestBed.inject(TaskChanges);
  });

  it('reads the task by its key, then the columns in order and the members to choose from', async () => {
    await store.load('KAN-1');

    expect(api['byKey']).toHaveBeenCalledWith('KAN-1', false);
    expect(store.state()).toBe('ready');
    expect(store.task()?.title).toBe('Login SSO');
    expect(store.columns().map((column) => column.name)).toEqual(['De făcut', 'În lucru', 'Gata']);
    expect(store.assignees().map((member) => member.displayName)).toEqual([
      'Florin Danci',
      'Ana Pop',
    ]);
  });

  it('keeps a deactivated assignee among the choices, so the panel still shows who has it', async () => {
    api['byKey'].mockReturnValueOnce(
      of(
        aTask({
          assignees: [
            { userId: 'u2', displayName: 'Ana Pop' },
            { userId: 'u3', displayName: 'Bob Ionescu' },
          ],
        }),
      ),
    );

    await store.load('KAN-1');

    expect(store.assignees().map((member) => [member.displayName, member.isActive])).toEqual([
      ['Florin Danci', true],
      ['Ana Pop', true],
      ['Bob Ionescu', false],
    ]);
  });

  it('asks for archived tasks too when the user may restore them', async () => {
    held.set(['tasks.archive']);

    await store.load('KAN-1');

    expect(api['byKey']).toHaveBeenCalledWith('KAN-1', true);
  });

  it('reads no choices for a task the user can only look at', async () => {
    api['byKey'].mockReturnValueOnce(of(aTask({ canChange: false })));

    await store.load('KAN-1');

    expect(api['project']).not.toHaveBeenCalled();
    expect(api['members']).not.toHaveBeenCalled();
  });

  it('says when the task is missing, closed to the user, or could not be read', async () => {
    api['byKey'].mockReturnValueOnce(refused(404, 'Task.NotFound'));
    await store.load('KAN-9');
    expect(store.state()).toBe('missing');

    api['byKey'].mockReturnValueOnce(refused(403, 'Project.MemberRequired'));
    await store.load('KAN-8');
    expect(store.state()).toBe('forbidden');

    api['byKey'].mockReturnValueOnce(refused(500, 'Unexpected'));
    await store.load('KAN-7');
    expect(store.state()).toBe('error');
  });

  it('lands only the last task asked for, however the answers arrive', async () => {
    const first = new Subject<TaskItemDto>();
    const second = new Subject<TaskItemDto>();
    api['byKey'].mockReturnValueOnce(first).mockReturnValueOnce(second);

    const loadingFirst = store.load('KAN-1');
    const loadingSecond = store.load('KAN-2');
    second.next(aTask({ key: 'KAN-2', title: 'Al doilea' }));
    second.complete();
    await loadingSecond;
    first.next(aTask({ key: 'KAN-1', title: 'Primul' }));
    first.complete();
    await loadingFirst;

    expect(store.task()?.title).toBe('Al doilea');
  });

  it('keeps the task the API answers a change with, and counts the change', async () => {
    await store.load('KAN-1');
    const before = changes.version();

    const outcome = await store.rename('Login cu SSO');

    expect(outcome).toEqual({ saved: true });
    expect(api['rename']).toHaveBeenCalledWith('t1', 'Login cu SSO');
    expect(store.task()?.title).toBe('Login cu SSO');
    expect(changes.version()).toBe(before + 1);
  });

  it('sends a blank description as none', async () => {
    await store.load('KAN-1');

    await store.describe('  \n ');

    expect(api['describe']).toHaveBeenCalledWith('t1', null);
  });

  it('tells which field the API refused, and keeps the task as it was', async () => {
    await store.load('KAN-1');
    api['rename'].mockReturnValueOnce(refused(400, 'Validation', { title: ['Too long.'] }));

    const outcome = await store.rename('x'.repeat(400));

    expect(outcome).toEqual({ saved: false, refusedField: 'title' });
    expect(store.task()?.title).toBe('Login SSO');
  });

  it('reads the task again when someone changed it meanwhile', async () => {
    await store.load('KAN-1');
    api['assign'].mockReturnValueOnce(refused(409, 'Task.ChangedMeanwhile'));
    api['byKey'].mockReturnValueOnce(of(aTask({ title: 'Schimbat de altcineva' })));

    const outcome = await store.assign(['u2']);
    await Promise.resolve();
    await Promise.resolve();

    expect(outcome).toEqual({ saved: false, refusedField: null });
    expect(api['byKey']).toHaveBeenCalledTimes(2);
    expect(store.task()?.title).toBe('Schimbat de altcineva');
  });

  // docs/SPEC.md §4.4: the whole list goes, and replaces the one before.
  it('sends the whole list of assignees and keeps what the API saved', async () => {
    await store.load('KAN-1');

    const outcome = await store.assign(['u2', 'me']);

    expect(outcome).toEqual({ saved: true });
    expect(api['assign']).toHaveBeenCalledWith('t1', ['u2', 'me']);
    expect(store.task()?.assignees.map((person) => person.userId)).toEqual(['u2', 'me']);
  });

  it('archives the task and counts the change', async () => {
    await store.load('KAN-1');
    const before = changes.version();

    expect(await store.archive()).toBe(true);

    expect(api['archive']).toHaveBeenCalledWith('t1');
    expect(changes.version()).toBe(before + 1);
  });

  it('offers the restore only on an archived task, to tasks.archive', async () => {
    api['byKey'].mockReturnValue(
      of(aTask({ archivedAt: '2026-11-06T08:00:00Z', canChange: false })),
    );
    await store.load('KAN-1');
    expect(store.canRestore()).toBe(false);

    held.set(['tasks.archive']);
    await store.load('KAN-1');
    expect(store.canRestore()).toBe(true);
  });
});
