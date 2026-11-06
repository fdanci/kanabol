import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RoleChanges, RoleDto } from './models';
import { RolesApi } from './roles-api';

export type LoadState = 'loading' | 'ready' | 'error';

function byName(a: RoleDto, b: RoleDto): number {
  return a.name.localeCompare(b.name, 'ro');
}

/**
 * The roles and the permissions the API knows. The writes throw the HttpErrorResponse of a refusal,
 * which errorInterceptor has shown already, for the page to keep the user's changes.
 */
@Injectable({ providedIn: 'root' })
export class RolesStore {
  private readonly api = inject(RolesApi);
  private readonly list = signal<RoleDto[]>([]);
  private readonly known = signal<string[]>([]);
  private readonly loadState = signal<LoadState>('loading');

  readonly roles = this.list.asReadonly();
  readonly permissions = this.known.asReadonly();
  readonly state = this.loadState.asReadonly();

  async load(): Promise<void> {
    this.loadState.set('loading');
    try {
      const [page, permissions] = await Promise.all([
        firstValueFrom(this.api.list()),
        firstValueFrom(this.api.permissions()),
      ]);
      this.list.set(page.items);
      this.known.set(permissions);
      this.loadState.set('ready');
    } catch {
      this.loadState.set('error');
    }
  }

  async create(changes: RoleChanges): Promise<RoleDto> {
    const role = await firstValueFrom(this.api.create(changes));
    this.list.update((roles) => [...roles, role].sort(byName));
    return role;
  }

  async update(id: string, changes: RoleChanges): Promise<RoleDto> {
    const role = await firstValueFrom(this.api.update(id, changes));
    this.list.update((roles) => roles.map((each) => (each.id === id ? role : each)).sort(byName));
    return role;
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.api.delete(id));
    this.list.update((roles) => roles.filter((role) => role.id !== id));
  }
}
