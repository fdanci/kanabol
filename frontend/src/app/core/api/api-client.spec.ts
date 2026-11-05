import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiClient } from './api-client';

describe('ApiClient', () => {
  let api: ApiClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads under the API path of the same origin', () => {
    let body: unknown;
    api.get<{ id: string }>('/users/1').subscribe((answer) => (body = answer));

    const request = http.expectOne('/api/users/1');
    expect(request.request.method).toBe('GET');
    request.flush({ id: '1' });

    expect(body).toEqual({ id: '1' });
  });

  it('sends query parameters', () => {
    api.get('/users', { params: { page: 2, pageSize: 50 } }).subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/users');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({});
  });

  it('sends a body with a post and a put', () => {
    api.post('/users', { email: 'ana@example.com' }).subscribe();
    api.put('/users/1', { displayName: 'Ana' }).subscribe();

    const post = http.expectOne((candidate) => candidate.method === 'POST');
    const put = http.expectOne((candidate) => candidate.method === 'PUT');
    expect(post.request.url).toBe('/api/users');
    expect(post.request.body).toEqual({ email: 'ana@example.com' });
    expect(put.request.url).toBe('/api/users/1');
    expect(put.request.body).toEqual({ displayName: 'Ana' });
    post.flush({});
    put.flush({});
  });

  it('deletes', () => {
    let done = false;
    api.delete('/roles/1').subscribe(() => (done = true));

    const request = http.expectOne('/api/roles/1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(done).toBe(true);
  });
});
