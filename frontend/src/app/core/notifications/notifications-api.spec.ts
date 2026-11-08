import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { QUIET } from '../api/error-interceptor';
import { NotificationsApi } from './notifications-api';
import { aNotification } from './notifications-testing';

/** docs/SPEC.md §4.4 "Notificările": the user's own, a page at a time, the count, read and read-all. */
describe('NotificationsApi', () => {
  let api: NotificationsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(NotificationsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('reads a page, with only the filters asked for', async () => {
    const notification = aNotification();
    const reading = firstValueFrom(api.list({ page: 2, pageSize: 50, unread: false, type: null }));

    const request = http.expectOne((sent) => sent.url === '/api/notifications');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.toString()).toBe('page=2&pageSize=50');
    expect(request.request.context.get(QUIET)).toBe(false);
    request.flush({ items: [notification], page: 2, pageSize: 50, totalCount: 51 });

    expect((await reading).items).toEqual([notification]);
  });

  it('asks for the unread ones of a type, quietly for the bell', () => {
    void firstValueFrom(
      api.list({ page: 1, pageSize: 10, unread: true, type: 'CommentAdded' }, true),
    );

    const request = http.expectOne((sent) => sent.url === '/api/notifications');
    expect(request.request.params.toString()).toBe(
      'page=1&pageSize=10&unread=true&type=CommentAdded',
    );
    expect(request.request.context.get(QUIET)).toBe(true);
    request.flush({ items: [], page: 1, pageSize: 10, totalCount: 0 });
  });

  it('reads the unread count quietly', async () => {
    const reading = firstValueFrom(api.unreadCount());

    const request = http.expectOne('/api/notifications/unread-count');
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(QUIET)).toBe(true);
    request.flush({ count: 3 });

    expect(await reading).toEqual({ count: 3 });
  });

  it('marks one read, and all of them', async () => {
    const reading = firstValueFrom(api.markRead('n1'));
    const read = http.expectOne('/api/notifications/n1/read');
    expect(read.request.method).toBe('POST');
    read.flush(aNotification({ readAt: '2026-11-08T10:00:00Z' }));
    expect((await reading).readAt).toBe('2026-11-08T10:00:00Z');

    const marking = firstValueFrom(api.markAllRead());
    const all = http.expectOne('/api/notifications/read-all');
    expect(all.request.method).toBe('POST');
    all.flush({ count: 4 });
    expect(await marking).toEqual({ count: 4 });
  });
});
