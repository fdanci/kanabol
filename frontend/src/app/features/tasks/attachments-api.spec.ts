import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, lastValueFrom, toArray } from 'rxjs';
import { AttachmentsApi } from './attachments-api';
import { anAttachment } from './task-testing';

/**
 * docs/SPEC.md §4.4 and §10: one file a request, as multipart in the field file, with its progress; the
 * file back as a blob, since the token isn't in a link; and DELETE for good.
 */
describe('AttachmentsApi', () => {
  let api: AttachmentsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AttachmentsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('sends one file in the field file, and reports its progress up to the answer', async () => {
    const file = new File(['%PDF'], 'brief.pdf', { type: 'application/pdf' });
    const events = lastValueFrom(api.upload('t1', file).pipe(toArray()));

    const request = http.expectOne('/api/tasks/t1/attachments');
    expect(request.request.method).toBe('POST');
    expect(request.request.reportUploadProgress).toBe(true);
    const body = request.request.body as FormData;
    expect(body.getAll('file')).toHaveLength(1);
    expect((body.get('file') as File).name).toBe('brief.pdf');
    request.event({ type: HttpEventType.UploadProgress, loaded: 2, total: 4 });
    request.flush(anAttachment());

    expect(await events).toEqual([
      { progress: 50, attachment: null },
      { progress: 100, attachment: anAttachment() },
    ]);
  });

  it('downloads a file as a blob', async () => {
    const downloading = firstValueFrom(api.download('a1'));

    const request = http.expectOne('/api/attachments/a1');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['%PDF']));

    expect(await (await downloading).text()).toBe('%PDF');
  });

  it('deletes one by its id', async () => {
    const deleting = firstValueFrom(api.delete('a1'));

    const request = http.expectOne('/api/attachments/a1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await deleting;
  });
});
