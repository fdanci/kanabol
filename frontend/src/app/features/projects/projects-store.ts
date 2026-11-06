import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../shared/models/page';
import { NewProject, ProjectDto, ProjectSummaryDto } from './models';
import { ProjectsApi } from './projects-api';

export type LoadState = 'loading' | 'ready' | 'error';

/** The first of the page sizes the paginator offers: 25, 50, 100 (docs/UI_UX_SPEC.md §6.4). */
export const PROJECTS_PAGE_SIZE = 25;

/** The page of projects on screen, with or without the archived ones. */
@Injectable({ providedIn: 'root' })
export class ProjectsStore {
  private readonly api = inject(ProjectsApi);
  private readonly current = signal<Page<ProjectSummaryDto> | null>(null);
  private readonly where = signal({
    page: 1,
    pageSize: PROJECTS_PAGE_SIZE,
    includeArchived: false,
  });
  private readonly loadState = signal<LoadState>('loading');
  private latest = 0;

  readonly projects = computed(() => this.current()?.items ?? []);
  readonly totalCount = computed(() => this.current()?.totalCount ?? 0);
  readonly page = computed(() => this.where().page);
  readonly pageSize = computed(() => this.where().pageSize);
  readonly includeArchived = computed(() => this.where().includeArchived);
  readonly state = this.loadState.asReadonly();

  /** Only the last page asked for lands, however the answers arrive. */
  async load(
    page = this.page(),
    pageSize = this.pageSize(),
    includeArchived = this.includeArchived(),
  ): Promise<void> {
    const request = ++this.latest;
    this.where.set({ page, pageSize, includeArchived });
    this.loadState.set('loading');
    try {
      const answer = await firstValueFrom(this.api.list(page, pageSize, includeArchived));
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

  /** Archived ones in or out: the list starts again from its first page. */
  showArchived(shown: boolean): Promise<void> {
    return this.load(1, this.pageSize(), shown);
  }

  /** Throws the HttpErrorResponse of a refusal, for the form to explain. The list is read again. */
  async create(project: NewProject): Promise<ProjectDto> {
    const created = await firstValueFrom(this.api.create(project));
    void this.load();
    return created;
  }
}
