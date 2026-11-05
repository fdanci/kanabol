import { HttpClient, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { correlationInterceptor } from './correlation-interceptor';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

describe('correlationInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('gives each API request an id of its own, which the API logs with it', () => {
    client.get('/api/users').subscribe();
    client.get('/api/roles').subscribe();

    const first = http.expectOne('/api/users').request.headers.get('X-Correlation-Id');
    const second = http.expectOne('/api/roles').request.headers.get('X-Correlation-Id');
    expect(first).toMatch(UUID);
    expect(second).toMatch(UUID);
    expect(first).not.toBe(second);
  });

  it('keeps an id the request already has', () => {
    client
      .get('/api/users', { headers: new HttpHeaders({ 'X-Correlation-Id': 'given-id' }) })
      .subscribe();

    expect(http.expectOne('/api/users').request.headers.get('X-Correlation-Id')).toBe('given-id');
  });

  it('leaves requests outside the API alone', () => {
    client.get('/brand/kanabol-mark.svg').subscribe();
    client.get('/apiary').subscribe();

    expect(http.expectOne('/brand/kanabol-mark.svg').request.headers.has('X-Correlation-Id')).toBe(
      false,
    );
    expect(http.expectOne('/apiary').request.headers.has('X-Correlation-Id')).toBe(false);
  });
});
