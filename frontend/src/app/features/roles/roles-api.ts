import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import { RoleChanges, RoleDto } from './models';

/** The largest page the API gives (docs/SPEC.md §7.3). Roles are few, so one page holds them all. */
export const ROLES_PAGE_SIZE = 100;

/** The roles and the permissions they can group (docs/SPEC.md §4.4). All of it needs roles.manage. */
@Injectable({ providedIn: 'root' })
export class RolesApi {
  private readonly api = inject(ApiClient);

  list(): Observable<Page<RoleDto>> {
    return this.api.get<Page<RoleDto>>('/roles', {
      params: { page: 1, pageSize: ROLES_PAGE_SIZE },
    });
  }

  permissions(): Observable<string[]> {
    return this.api.get<string[]>('/permissions');
  }

  /** A taken name goes under the name field, so the snackbar leaves it out. */
  create(changes: RoleChanges): Observable<RoleDto> {
    return this.api.post<RoleDto>('/roles', changes, { context: handledHere('Role.NameTaken') });
  }

  update(id: string, changes: RoleChanges): Observable<RoleDto> {
    return this.api.put<RoleDto>(`/roles/${id}`, changes, {
      context: handledHere('Role.NameTaken'),
    });
  }

  delete(id: string): Observable<null> {
    return this.api.delete<null>(`/roles/${id}`);
  }
}
