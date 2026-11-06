import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { problemIn } from '../../shared/models/problem-details';
import {
  MemberCandidateDto,
  ProjectChanges,
  ProjectDto,
  ProjectMemberDto,
  ProjectRole,
} from './models';
import { ProjectsApi } from './projects-api';

/** Missing and forbidden have pages of their own; error is anything else, with a retry. */
export type ProjectState = 'loading' | 'ready' | 'missing' | 'forbidden' | 'error';
export type MembersState = 'loading' | 'ready' | 'error';

/**
 * The project open on screen, and its members. One per project page (ProjectPage provides it), so
 * its views share it and another project starts from nothing. The writes throw the
 * HttpErrorResponse of a refusal, which errorInterceptor has shown already.
 */
@Injectable()
export class ProjectStore {
  private readonly api = inject(ProjectsApi);
  private readonly auth = inject(AuthStore);
  private readonly current = signal<ProjectDto | null>(null);
  private readonly loadState = signal<ProjectState>('loading');
  private readonly memberList = signal<ProjectMemberDto[]>([]);
  private readonly membersLoadState = signal<MembersState>('loading');
  private latest = 0;

  readonly project = this.current.asReadonly();
  readonly state = this.loadState.asReadonly();
  readonly members = this.memberList.asReadonly();
  readonly membersState = this.membersLoadState.asReadonly();
  readonly archived = computed(() => this.current()?.archivedAt != null);
  /** Changes need an owner, or projects.manage, and an open project. */
  readonly canChange = computed(() => (this.current()?.canManage ?? false) && !this.archived());
  /** A deactivated owner can't manage the project, so only the active ones count (SPEC section 4.4). */
  private readonly activeOwnerIds = computed(
    () =>
      new Set(
        this.memberList()
          .filter((member) => member.role === 'Owner' && member.isActive)
          .map((member) => member.userId),
      ),
  );

  /** Whether an active owner stays once this user is no longer one: the API refuses the change otherwise. */
  keepsAnActiveOwnerWithout(userId: string): boolean {
    return [...this.activeOwnerIds()].some((ownerId) => ownerId !== userId);
  }

  /** Only the last project asked for lands, however the answers arrive. */
  async load(id: string): Promise<void> {
    const request = ++this.latest;
    if (this.current()?.id !== id) {
      this.current.set(null);
      this.memberList.set([]);
      this.membersLoadState.set('loading');
    }
    this.loadState.set('loading');
    try {
      const project = await firstValueFrom(this.api.get(id));
      if (request === this.latest) {
        this.current.set(project);
        this.loadState.set('ready');
      }
    } catch (error) {
      if (request === this.latest) {
        this.loadState.set(stateOf(error));
      }
    }
  }

  async loadMembers(): Promise<void> {
    const id = this.id();
    this.membersLoadState.set('loading');
    try {
      this.memberList.set(await firstValueFrom(this.api.members(id)));
      this.membersLoadState.set('ready');
    } catch {
      this.membersLoadState.set('error');
    }
  }

  async update(changes: ProjectChanges): Promise<ProjectDto> {
    const saved = await firstValueFrom(this.api.update(this.id(), changes));
    this.current.set(saved);
    return saved;
  }

  /** The API answers 204, so the project is read again, with its archive date. */
  async archive(): Promise<void> {
    const id = this.id();
    await firstValueFrom(this.api.archive(id));
    await this.load(id);
  }

  async restore(): Promise<void> {
    this.current.set(await firstValueFrom(this.api.restore(this.id())));
  }

  candidates(search: string): Promise<MemberCandidateDto[]> {
    return firstValueFrom(this.api.candidates(this.id(), search));
  }

  /** The list is read again, in the API's order: owners first, then by name. */
  async addMember(userId: string, role: ProjectRole): Promise<ProjectMemberDto> {
    const added = await firstValueFrom(this.api.addMember(this.id(), userId, role));
    await this.loadMembers();
    return added;
  }

  async changeMemberRole(userId: string, role: ProjectRole): Promise<ProjectMemberDto> {
    const changed = await firstValueFrom(this.api.changeMemberRole(this.id(), userId, role));
    await this.loadMembers();
    await this.reloadIfAbout(userId);
    return changed;
  }

  /** Someone who removes themselves may lose the project: it's read again to know. */
  async removeMember(userId: string): Promise<void> {
    await firstValueFrom(this.api.removeMember(this.id(), userId));
    this.memberList.update((members) => members.filter((member) => member.userId !== userId));
    await this.reloadIfAbout(userId);
  }

  private async reloadIfAbout(userId: string): Promise<void> {
    if (userId === this.auth.user()?.id) {
      await this.load(this.id());
    }
  }

  private id(): string {
    const project = this.current();
    if (!project) {
      throw new Error('No project is open.');
    }
    return project.id;
  }
}

function stateOf(error: unknown): ProjectState {
  if (!(error instanceof HttpErrorResponse)) {
    return 'error';
  }
  const code = problemIn(error)?.code;
  if (error.status === 404 || code === 'Project.NotFound') {
    return 'missing';
  }
  if (code === 'Project.MemberRequired') {
    return 'forbidden';
  }
  return 'error';
}
