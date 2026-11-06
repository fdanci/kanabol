import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import { AssignableRoleDto, NewUser, UserChanges, UserDto } from './models';

/** The users (docs/SPEC.md §4.4). All of it needs users.manage. */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly api = inject(ApiClient);

  list(page: number, pageSize: number): Observable<Page<UserDto>> {
    return this.api.get<Page<UserDto>>('/users', { params: { page, pageSize } });
  }

  /** Only the roles whose permissions the caller has: nobody gives more than they have. */
  assignableRoles(): Observable<AssignableRoleDto[]> {
    return this.api.get<AssignableRoleDto[]>('/users/assignable-roles');
  }

  /** A taken email goes under the email field, so the snackbar leaves it out. */
  create(user: NewUser): Observable<UserDto> {
    return this.api.post<UserDto>('/users', user, { context: handledHere('User.EmailTaken') });
  }

  update(id: string, changes: UserChanges): Observable<UserDto> {
    return this.api.put<UserDto>(`/users/${id}`, changes, {
      context: handledHere('User.EmailTaken'),
    });
  }

  changeRole(id: string, roleId: string): Observable<UserDto> {
    return this.api.put<UserDto>(`/users/${id}/role`, { roleId });
  }

  deactivate(id: string): Observable<UserDto> {
    return this.api.post<UserDto>(`/users/${id}/deactivate`);
  }

  activate(id: string): Observable<UserDto> {
    return this.api.post<UserDto>(`/users/${id}/activate`);
  }

  resetPassword(id: string, initialPassword: string): Observable<UserDto> {
    return this.api.post<UserDto>(`/users/${id}/reset-password`, { initialPassword });
  }
}
