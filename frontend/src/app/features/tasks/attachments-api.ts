import { HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { AttachmentDto } from './models';

/** How far a file went up, from 0 to 100, and the attachment once the API has it. */
export interface UploadProgress {
  progress: number;
  attachment: AttachmentDto | null;
}

/**
 * Files on a task (docs/SPEC.md §4.4, §10): one a request, as multipart in the field file, up to 25 MB
 * with an extension the API allows; downloaded by whoever sees the task; deleted, file and all, by whoever
 * uploaded it or tasks.archive.
 */
@Injectable({ providedIn: 'root' })
export class AttachmentsApi {
  private readonly api = inject(ApiClient);

  /** Its progress as it goes, then the attachment, and done. Unsubscribing stops it. */
  upload(taskId: string, file: File): Observable<UploadProgress> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.api.upload<AttachmentDto>(`/tasks/${taskId}/attachments`, form).pipe(
      map((event): UploadProgress | null => {
        if (event.type === HttpEventType.UploadProgress) {
          const total = event.total ?? file.size;
          return {
            progress: total > 0 ? Math.min(99, Math.round((event.loaded / total) * 100)) : 0,
            attachment: null,
          };
        }
        if (event.type === HttpEventType.Response && event.body) {
          return { progress: 100, attachment: event.body };
        }
        return null;
      }),
      filter((progress) => progress !== null),
    );
  }

  download(id: string): Observable<Blob> {
    return this.api.blob(`/attachments/${id}`);
  }

  delete(id: string): Observable<void> {
    return this.api.delete(`/attachments/${id}`);
  }
}
