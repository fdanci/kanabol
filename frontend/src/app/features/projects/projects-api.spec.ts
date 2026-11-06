import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HANDLED_CODES } from '../../core/api/error-interceptor';
import { ProjectDto, ProjectMemberDto } from './models';
import { ProjectsApi } from './projects-api';

const KANABOL: ProjectDto = {
  id: 'p1',
  name: 'Kanabol',
  key: 'KAN',
  description: null,
  createdAt: '2026-11-04T08:00:00Z',
  archivedAt: null,
  myRole: 'Owner',
  canManage: true,
  columns: [],
};

const ANA: ProjectMemberDto = {
  userId: 'u2',
  email: 'ana@kanabol.test',
  displayName: 'Ana Pop',
  role: 'Member',
  isActive: true,
};

describe('ProjectsApi', () => {
  let api: ProjectsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ProjectsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  function expectRequest(method: string, url: string, body: unknown = null) {
    const request = http.expectOne((r) => r.url === url);
    expect(request.request.method).toBe(method);
    expect(request.request.body).toEqual(body);
    return request;
  }

  it('reads a page of projects, with or without the archived ones', async () => {
    const answer = firstValueFrom(api.list(2, 25, true));
    const request = expectRequest('GET', '/api/projects');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('includeArchived')).toBe('true');
    request.flush({ items: [], page: 2, pageSize: 25, totalCount: 30 });
    expect((await answer).totalCount).toBe(30);
  });

  it('reads one project, and lets its page say why it can not', async () => {
    const answer = firstValueFrom(api.get('p1'));
    const request = expectRequest('GET', '/api/projects/p1');
    expect(request.request.context.get(HANDLED_CODES)).toEqual([
      'Project.NotFound',
      'Project.MemberRequired',
    ]);
    request.flush(KANABOL);
    expect(await answer).toEqual(KANABOL);
  });

  it('creates a project, and leaves a taken key to the form', async () => {
    const answer = firstValueFrom(api.create({ name: 'Kanabol', key: 'KAN', description: null }));
    const request = expectRequest('POST', '/api/projects', {
      name: 'Kanabol',
      key: 'KAN',
      description: null,
    });
    expect(request.request.context.get(HANDLED_CODES)).toEqual(['Project.KeyTaken']);
    request.flush(KANABOL);
    expect((await answer).id).toBe('p1');
  });

  it('changes the name and the description, never the key', async () => {
    const answer = firstValueFrom(api.update('p1', { name: 'Kanabol v2', description: 'Nou' }));
    expectRequest('PUT', '/api/projects/p1', { name: 'Kanabol v2', description: 'Nou' }).flush({
      ...KANABOL,
      name: 'Kanabol v2',
    });
    expect((await answer).name).toBe('Kanabol v2');
  });

  it('archives with DELETE and restores with a POST', async () => {
    const archived = firstValueFrom(api.archive('p1'));
    expectRequest('DELETE', '/api/projects/p1').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    await archived;

    const restored = firstValueFrom(api.restore('p1'));
    expectRequest('POST', '/api/projects/p1/restore').flush(KANABOL);
    expect((await restored).archivedAt).toBeNull();
  });

  it('reads, adds, changes and removes members', async () => {
    const members = firstValueFrom(api.members('p1'));
    expectRequest('GET', '/api/projects/p1/members').flush([ANA]);
    expect(await members).toEqual([ANA]);

    const added = firstValueFrom(api.addMember('p1', 'u2', 'Member'));
    expectRequest('POST', '/api/projects/p1/members', { userId: 'u2', role: 'Member' }).flush(ANA);
    expect((await added).userId).toBe('u2');

    const changed = firstValueFrom(api.changeMemberRole('p1', 'u2', 'Owner'));
    expectRequest('PUT', '/api/projects/p1/members/u2', { role: 'Owner' }).flush({
      ...ANA,
      role: 'Owner',
    });
    expect((await changed).role).toBe('Owner');

    const removed = firstValueFrom(api.removeMember('p1', 'u2'));
    expectRequest('DELETE', '/api/projects/p1/members/u2').flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    await removed;
  });

  it('looks for who can join, with the search when there is one', async () => {
    const all = firstValueFrom(api.candidates('p1', ''));
    const plain = expectRequest('GET', '/api/projects/p1/member-candidates');
    expect(plain.request.params.has('search')).toBe(false);
    plain.flush([]);
    await all;

    const some = firstValueFrom(api.candidates('p1', ' dan '));
    const searched = expectRequest('GET', '/api/projects/p1/member-candidates');
    expect(searched.request.params.get('search')).toBe('dan');
    searched.flush([{ id: 'u3', email: 'dan@kanabol.test', displayName: 'Dan' }]);
    expect(await some).toHaveLength(1);
  });
});
