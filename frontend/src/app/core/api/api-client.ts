import { HttpClient, HttpContext, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface RequestOptions {
  params?: HttpParams | Record<string, string | number | boolean>;
  context?: HttpContext;
}

/** The only door to the API: it puts the base path in front, and the interceptors do the rest. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: string, options?: RequestOptions): Observable<T> {
    return this.http.get<T>(this.url(path), options);
  }

  post<T>(path: string, body: unknown = null, options?: RequestOptions): Observable<T> {
    return this.http.post<T>(this.url(path), body, options);
  }

  put<T>(path: string, body: unknown, options?: RequestOptions): Observable<T> {
    return this.http.put<T>(this.url(path), body, options);
  }

  patch<T>(path: string, body: unknown, options?: RequestOptions): Observable<T> {
    return this.http.patch<T>(this.url(path), body, options);
  }

  delete<T = void>(path: string, options?: RequestOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), options);
  }

  /** A form, such as a file, with every event on the way: the upload's progress, then the answer. */
  upload<T>(path: string, form: FormData, options?: RequestOptions): Observable<HttpEvent<T>> {
    return this.http.post<T>(this.url(path), form, {
      ...options,
      observe: 'events',
      reportUploadProgress: true,
    });
  }

  /** A file, which a link can't fetch: the token is in memory, not in a cookie. */
  blob(path: string, options?: RequestOptions): Observable<Blob> {
    return this.http.get(this.url(path), { ...options, responseType: 'blob' });
  }

  private url(path: string): string {
    return `${environment.apiBaseUrl}${path}`;
  }
}

/** Whether a request goes to the API, for the interceptors. */
export function isApiRequest(url: string): boolean {
  return url.startsWith(`${environment.apiBaseUrl}/`);
}
