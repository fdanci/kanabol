import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../shared/models/page';
import { AssignableRoleDto, NewUser, UserChanges, UserDto } from './models';
import { UsersApi } from './users-api';

export type LoadState = 'loading' | 'ready' | 'error';

/** The first of the page sizes the paginator offers: 25, 50, 100 (docs/UI_UX_SPEC.md §6.4). */
export const USERS_PAGE_SIZE = 25;

/**
 * The page of users on screen. A change to one user replaces its row with the API's answer. The
 * writes throw the HttpErrorResponse of a refusal, which errorInterceptor has shown already.
 */
@Injectable({ providedIn: 'root' })
export class UsersStore {
  private readonly api = inject(UsersApi);
  private readonly current = signal<Page<UserDto> | null>(null);
  private readonly where = signal({ page: 1, pageSize: USERS_PAGE_SIZE });
  private readonly loadState = signal<LoadState>('loading');
  private latest = 0;

  readonly users = computed(() => this.current()?.items ?? []);
  readonly totalCount = computed(() => this.current()?.totalCount ?? 0);
  readonly page = computed(() => this.where().page);
  readonly pageSize = computed(() => this.where().pageSize);
  readonly state = this.loadState.asReadonly();

  /** Only the last page asked for lands, however the answers arrive. */
  async load(page = this.page(), pageSize = this.pageSize()): Promise<void> {
    const request = ++this.latest;
    this.where.set({ page, pageSize });
    this.loadState.set('loading');
    try {
      const answer = await firstValueFrom(this.api.list(page, pageSize));
      if (request === this.latest) {
        this.current.set(answer);
        this.loadState.set('ready');
      }
    } catch {
      if (request === this.latest) {
        this.loadState.set('error');
      }
    }
  }

  assignableRoles(): Promise<AssignableRoleDto[]> {
    return firstValueFrom(this.api.assignableRoles());
  }

  /** The new user lands where the API's order by email puts it, so the page is read again. */
  async create(user: NewUser): Promise<UserDto> {
    const created = await firstValueFrom(this.api.create(user));
    await this.load();
    return created;
  }

  update(id: string, changes: UserChanges): Promise<UserDto> {
    return this.replace(firstValueFrom(this.api.update(id, changes)));
  }

  changeRole(id: string, roleId: string): Promise<UserDto> {
    return this.replace(firstValueFrom(this.api.changeRole(id, roleId)));
  }

  deactivate(id: string): Promise<UserDto> {
    return this.replace(firstValueFrom(this.api.deactivate(id)));
  }

  activate(id: string): Promise<UserDto> {
    return this.replace(firstValueFrom(this.api.activate(id)));
  }

  resetPassword(id: string, initialPassword: string): Promise<UserDto> {
    return this.replace(firstValueFrom(this.api.resetPassword(id, initialPassword)));
  }

  private async replace(answer: Promise<UserDto>): Promise<UserDto> {
    const user = await answer;
    this.current.update(
      (page) =>
        page && { ...page, items: page.items.map((each) => (each.id === user.id ? user : each)) },
    );
    return user;
  }
}
