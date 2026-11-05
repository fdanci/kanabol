import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
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

  delete<T = void>(path: string, options?: RequestOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), options);
  }

  private url(path: string): string {
    return `${environment.apiBaseUrl}${path}`;
  }
}

/** Whether a request goes to the API, for the interceptors. */
export function isApiRequest(url: string): boolean {
  return url.startsWith(`${environment.apiBaseUrl}/`);
}
