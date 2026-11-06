import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HANDLED_CODES } from '../../core/api/error-interceptor';
import { UserDto } from './models';
import { UsersApi } from './users-api';

const ANA: UserDto = {
  id: 'u2',
  email: 'ana@kanabol.test',
  displayName: 'Ana Pop',
  roleId: 'r3',
  roleName: 'Membru',
  isActive: true,
  mustChangePassword: false,
  lockedUntil: null,
  createdAt: '2026-11-04T08:00:00Z',
};

describe('UsersApi', () => {
  let api: UsersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(UsersApi);
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

  it('reads a page of users', async () => {
    const answer = firstValueFrom(api.list(3, 50));
    const request = expectRequest('GET', '/api/users');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [ANA], page: 3, pageSize: 50, totalCount: 101 });

    expect((await answer).totalCount).toBe(101);
  });

  it('reads the roles the caller may give', async () => {
    const answer = firstValueFrom(api.assignableRoles());
    expectRequest('GET', '/api/users/assignable-roles').flush([{ id: 'r3', name: 'Membru' }]);

    expect(await answer).toEqual([{ id: 'r3', name: 'Membru' }]);
  });

  it('creates and edits a user, and shows a taken email under its field', async () => {
    const created = firstValueFrom(
      api.create({ email: 'a@b', displayName: 'A', roleId: 'r3', initialPassword: 'p' }),
    );
    const create = expectRequest('POST', '/api/users', {
      email: 'a@b',
      displayName: 'A',
      roleId: 'r3',
      initialPassword: 'p',
    });
    expect(create.request.context.get(HANDLED_CODES)).toEqual(['User.EmailTaken']);
    create.flush(ANA, { status: 201, statusText: 'Created' });
    await created;

    const updated = firstValueFrom(api.update('u2', { email: 'a@b', displayName: 'A' }));
    const update = expectRequest('PUT', '/api/users/u2', { email: 'a@b', displayName: 'A' });
    expect(update.request.context.get(HANDLED_CODES)).toEqual(['User.EmailTaken']);
    update.flush(ANA);
    await updated;
  });

  it('changes the role, deactivates, activates and resets the password', async () => {
    const calls: [Promise<UserDto>, string, string, unknown][] = [
      [firstValueFrom(api.changeRole('u2', 'r2')), 'PUT', '/api/users/u2/role', { roleId: 'r2' }],
      [firstValueFrom(api.deactivate('u2')), 'POST', '/api/users/u2/deactivate', null],
      [firstValueFrom(api.activate('u2')), 'POST', '/api/users/u2/activate', null],
      [
        firstValueFrom(api.resetPassword('u2', 'o parola noua')),
        'POST',
        '/api/users/u2/reset-password',
        { initialPassword: 'o parola noua' },
      ],
    ];
    for (const [answer, method, url, body] of calls) {
      expectRequest(method, url, body).flush(ANA);
      expect(await answer).toEqual(ANA);
    }
  });
});
