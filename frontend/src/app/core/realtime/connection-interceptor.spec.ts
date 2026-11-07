import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CONNECTION_HEADER, connectionInterceptor } from './connection-interceptor';
import { TabConnection } from './tab-connection';

/** docs/SPEC.md §9: every API request carries the tab's hub connection, when it has one. */
describe('connectionInterceptor', () => {
  const connectionId = signal<string | null>(null);
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    connectionId.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([connectionInterceptor])),
        provideHttpClientTesting(),
        { provide: TabConnection, useValue: { id: connectionId } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
  });

  function headerOf(url: string): string | null {
    http.get(url).subscribe();
    const request = backend.expectOne(url);
    request.flush({});
    return request.request.headers.get(CONNECTION_HEADER);
  }

  it('sends the connection id with an API request', () => {
    connectionId.set('sJ0KHkCgN1vD-9_qW2xZtA');

    expect(CONNECTION_HEADER).toBe('X-Connection-Id');
    expect(headerOf('/api/projects/p1/board')).toBe('sJ0KHkCgN1vD-9_qW2xZtA');
  });

  it('sends none without a connection', () => {
    expect(headerOf('/api/projects/p1/board')).toBeNull();
  });

  it('sends none outside the API', () => {
    connectionId.set('sJ0KHkCgN1vD-9_qW2xZtA');

    expect(headerOf('/assets/i18n.json')).toBeNull();
  });
});
