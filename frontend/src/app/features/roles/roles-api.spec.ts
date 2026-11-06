import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HANDLED_CODES } from '../../core/api/error-interceptor';
import { RoleDto } from './models';
import { RolesApi } from './roles-api';

const ROLE: RoleDto = {
  id: 'r1',
  name: 'Lead',
  isSystem: false,
  permissions: ['tasks.create'],
  userCount: 2,
};

describe('RolesApi', () => {
  let api: RolesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(RolesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads every role in one page of the largest size the API allows', async () => {
    const answer = firstValueFrom(api.list());
    const request = http.expectOne((r) => r.url === '/api/roles');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('100');
    request.flush({ items: [ROLE], page: 1, pageSize: 100, totalCount: 1 });

    expect((await answer).items).toEqual([ROLE]);
  });

  it('reads the permissions the API knows', async () => {
    const answer = firstValueFrom(api.permissions());
    http.expectOne('/api/permissions').flush(['users.manage']);

    expect(await answer).toEqual(['users.manage']);
  });

  it('creates a role, and shows a taken name under the field itself', async () => {
    const answer = firstValueFrom(api.create({ name: 'Lead', permissions: ['tasks.create'] }));
    const request = http.expectOne('/api/roles');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Lead', permissions: ['tasks.create'] });
    expect(request.request.context.get(HANDLED_CODES)).toEqual(['Role.NameTaken']);
    request.flush(ROLE, { status: 201, statusText: 'Created' });

    expect(await answer).toEqual(ROLE);
  });

  it('updates a role with PUT, the same way', async () => {
    const answer = firstValueFrom(api.update('r1', { name: 'Lead', permissions: [] }));
    const request = http.expectOne('/api/roles/r1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Lead', permissions: [] });
    expect(request.request.context.get(HANDLED_CODES)).toEqual(['Role.NameTaken']);
    request.flush(ROLE);

    expect(await answer).toEqual(ROLE);
  });

  it('deletes a role', async () => {
    const answer = firstValueFrom(api.delete('r1'));
    const request = http.expectOne('/api/roles/r1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await answer;
  });
});
