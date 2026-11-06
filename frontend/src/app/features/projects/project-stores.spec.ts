import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Page } from '../../shared/models/page';
import { ProjectDto, ProjectMemberDto, ProjectSummaryDto } from './models';
import { ProjectStore } from './project-store';
import { ProjectsApi } from './projects-api';
import { ProjectsStore } from './projects-store';

function summary(id: string): ProjectSummaryDto {
  return {
    id,
    name: id,
    key: id.toUpperCase(),
    myRole: 'Member',
    owners: [],
    memberPreview: [],
    memberCount: 1,
    openTaskCount: 0,
    overdueTaskCount: 0,
    updatedAt: '2026-11-04T08:00:00Z',
    archivedAt: null,
  };
}

function project(extra: Partial<ProjectDto> = {}): ProjectDto {
  return {
    id: 'p1',
    name: 'Kanabol',
    key: 'KAN',
    description: null,
    createdAt: '2026-11-04T08:00:00Z',
    archivedAt: null,
    myRole: 'Owner',
    canManage: true,
    columns: [],
    ...extra,
  };
}

function member(userId: string, role: 'Owner' | 'Member' = 'Member'): ProjectMemberDto {
  return { userId, email: `${userId}@kanabol.test`, displayName: userId, role, isActive: true };
}

function refused(status: number, code: string): Observable<never> {
  return throwError(
    () => new HttpErrorResponse({ status, error: { code }, url: '/api/projects/p1' }),
  );
}

describe('ProjectsStore', () => {
  let answers: Subject<Page<ProjectSummaryDto>>[];
  let list: ReturnType<typeof vi.fn>;
  let store: ProjectsStore;

  beforeEach(() => {
    answers = [];
    list = vi.fn(() => {
      const answer = new Subject<Page<ProjectSummaryDto>>();
      answers.push(answer);
      return answer;
    });
    TestBed.configureTestingModule({
      providers: [{ provide: ProjectsApi, useValue: { list } }],
    });
    store = TestBed.inject(ProjectsStore);
  });

  function answer(index: number, page: number, ids: string[]): void {
    answers[index].next({ items: ids.map(summary), page, pageSize: 25, totalCount: 30 });
    answers[index].complete();
  }

  it('shows the last page asked for, even when an earlier answer arrives after it', async () => {
    const first = store.load(1);
    const second = store.load(2);

    answer(1, 2, ['b']);
    await second;
    answer(0, 1, ['a']);
    await first;

    expect(store.page()).toBe(2);
    expect(store.projects().map((each) => each.id)).toEqual(['b']);
    expect(store.state()).toBe('ready');
  });

  it('starts again from the first page when the archived ones come in or go out', async () => {
    const first = store.load(3, 50);
    answer(0, 3, ['a']);
    await first;

    const shown = store.showArchived(true);
    answer(1, 1, ['a', 'b']);
    await shown;

    expect(list).toHaveBeenLastCalledWith(1, 50, true);
    expect(store.includeArchived()).toBe(true);
    expect(store.page()).toBe(1);
  });

  it('says when the page could not be read', async () => {
    const loading = store.load(1);
    answers[0].error(new HttpErrorResponse({ status: 500 }));
    await loading;

    expect(store.state()).toBe('error');
  });
});

describe('ProjectStore', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let store: ProjectStore;

  beforeEach(() => {
    api = {
      get: vi.fn(() => of(project())),
      members: vi.fn(() => of([member('me', 'Owner'), member('ana')])),
      update: vi.fn((_id: string, changes: object) => of(project(changes))),
      archive: vi.fn(() => of(undefined)),
      restore: vi.fn(() => of(project())),
      addMember: vi.fn((_id: string, userId: string) => of(member(userId))),
      changeMemberRole: vi.fn((_id: string, userId: string, role: 'Owner' | 'Member') =>
        of(member(userId, role)),
      ),
      removeMember: vi.fn(() => of(undefined)),
      candidates: vi.fn(() => of([])),
    };
    TestBed.configureTestingModule({
      providers: [
        ProjectStore,
        { provide: ProjectsApi, useValue: api },
        { provide: AuthStore, useValue: { user: signal({ id: 'me' }) } },
      ],
    });
    store = TestBed.inject(ProjectStore);
  });

  it('reads the project, and lets an owner of an open one change it', async () => {
    await store.load('p1');

    expect(store.state()).toBe('ready');
    expect(store.project()?.key).toBe('KAN');
    expect(store.canChange()).toBe(true);
  });

  it('lets nobody change an archived project, nor a member an open one', async () => {
    api['get'].mockReturnValueOnce(of(project({ archivedAt: '2026-11-05T08:00:00Z' })));
    await store.load('p1');
    expect(store.archived()).toBe(true);
    expect(store.canChange()).toBe(false);

    api['get'].mockReturnValueOnce(of(project({ myRole: 'Member', canManage: false })));
    await store.load('p1');
    expect(store.canChange()).toBe(false);
  });

  it('tells a missing project from a closed one and from a failure', async () => {
    api['get'].mockReturnValueOnce(refused(404, 'Project.NotFound'));
    await store.load('p1');
    expect(store.state()).toBe('missing');

    api['get'].mockReturnValueOnce(refused(403, 'Project.MemberRequired'));
    await store.load('p1');
    expect(store.state()).toBe('forbidden');

    api['get'].mockReturnValueOnce(refused(500, 'Unexpected'));
    await store.load('p1');
    expect(store.state()).toBe('error');
  });

  it('forgets the last project when another one opens', async () => {
    await store.load('p1');
    await store.loadMembers();
    api['get'].mockReturnValueOnce(new Subject<ProjectDto>());

    void store.load('p2');

    expect(store.project()).toBeNull();
    expect(store.members()).toEqual([]);
  });

  it('reads the members, and knows when an owner is the last active one', async () => {
    await store.load('p1');
    await store.loadMembers();

    expect(store.members().map((each) => each.userId)).toEqual(['me', 'ana']);
    expect(store.keepsAnActiveOwnerWithout('me')).toBe(false);
    expect(store.keepsAnActiveOwnerWithout('ana')).toBe(true);
  });

  it('counts only the active owners as staying (SPEC section 4.4)', async () => {
    api['members'].mockReturnValueOnce(
      of([member('me', 'Owner'), { ...member('bob', 'Owner'), isActive: false }, member('ana')]),
    );
    await store.load('p1');
    await store.loadMembers();

    expect(store.keepsAnActiveOwnerWithout('me')).toBe(false);
    expect(store.keepsAnActiveOwnerWithout('bob')).toBe(true);
  });

  it('knows that no owner stays when none of them is active', async () => {
    api['members'].mockReturnValueOnce(
      of([{ ...member('bob', 'Owner'), isActive: false }, member('ana')]),
    );
    await store.load('p1');
    await store.loadMembers();

    expect(store.keepsAnActiveOwnerWithout('bob')).toBe(false);
  });

  it('saves the name and description, keeping the answer', async () => {
    await store.load('p1');

    await store.update({ name: 'Kanabol v2', description: 'Nou' });

    expect(api['update']).toHaveBeenCalledWith('p1', { name: 'Kanabol v2', description: 'Nou' });
    expect(store.project()?.name).toBe('Kanabol v2');
  });

  it('reads the project again after archiving it, for the date', async () => {
    await store.load('p1');
    api['get'].mockReturnValueOnce(of(project({ archivedAt: '2026-11-06T08:00:00Z' })));

    await store.archive();

    expect(api['archive']).toHaveBeenCalledWith('p1');
    expect(store.archived()).toBe(true);
  });

  it('reads the members again after adding one, in the API order', async () => {
    await store.load('p1');

    await store.addMember('dan', 'Member');

    expect(api['addMember']).toHaveBeenCalledWith('p1', 'dan', 'Member');
    expect(api['members']).toHaveBeenCalledTimes(1);
  });

  it('removes a member from the list, and reads the project again only when it was me', async () => {
    await store.load('p1');
    await store.loadMembers();

    await store.removeMember('ana');
    expect(store.members().map((each) => each.userId)).toEqual(['me']);
    expect(api['get']).toHaveBeenCalledTimes(1);

    await store.removeMember('me');
    expect(api['get']).toHaveBeenCalledTimes(2);
  });

  it('reads the project again when my own role changed, since what I may do changed', async () => {
    await store.load('p1');

    await store.changeMemberRole('ana', 'Owner');
    expect(api['get']).toHaveBeenCalledTimes(1);

    await store.changeMemberRole('me', 'Member');
    expect(api['get']).toHaveBeenCalledTimes(2);
  });

  it('passes a refusal on, for errorInterceptor has shown it', async () => {
    await store.load('p1');
    api['removeMember'].mockReturnValueOnce(refused(409, 'Project.LastOwner'));

    await expect(store.removeMember('me')).rejects.toBeInstanceOf(HttpErrorResponse);
  });
});
