import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../core/api/api-client';
import { handledHere } from '../../core/api/error-interceptor';
import { Page } from '../../shared/models/page';
import {
  MemberCandidateDto,
  NewProject,
  ProjectChanges,
  ProjectDto,
  ProjectMemberDto,
  ProjectRole,
  ProjectSummaryDto,
} from './models';

/**
 * The projects and their members (docs/SPEC.md §4.4). Creating one needs projects.create; the rest
 * depends on the caller's role on the project, which the API checks on every request.
 */
@Injectable({ providedIn: 'root' })
export class ProjectsApi {
  private readonly api = inject(ApiClient);

  list(
    page: number,
    pageSize: number,
    includeArchived: boolean,
  ): Observable<Page<ProjectSummaryDto>> {
    return this.api.get<Page<ProjectSummaryDto>>('/projects', {
      params: { page, pageSize, includeArchived },
    });
  }

  /** The project page says itself that a project is gone or closed to the caller. */
  get(id: string): Observable<ProjectDto> {
    return this.api.get<ProjectDto>(`/projects/${id}`, {
      context: handledHere('Project.NotFound', 'Project.MemberRequired'),
    });
  }

  /** A taken key goes under the key field, so the snackbar leaves it out. */
  create(project: NewProject): Observable<ProjectDto> {
    return this.api.post<ProjectDto>('/projects', project, {
      context: handledHere('Project.KeyTaken'),
    });
  }

  update(id: string, changes: ProjectChanges): Observable<ProjectDto> {
    return this.api.put<ProjectDto>(`/projects/${id}`, changes);
  }

  /** DELETE archives: nothing deletes a project (docs/SPEC.md §5.1). */
  archive(id: string): Observable<void> {
    return this.api.delete(`/projects/${id}`);
  }

  restore(id: string): Observable<ProjectDto> {
    return this.api.post<ProjectDto>(`/projects/${id}/restore`);
  }

  members(id: string): Observable<ProjectMemberDto[]> {
    return this.api.get<ProjectMemberDto[]>(`/projects/${id}/members`);
  }

  addMember(id: string, userId: string, role: ProjectRole): Observable<ProjectMemberDto> {
    return this.api.post<ProjectMemberDto>(`/projects/${id}/members`, { userId, role });
  }

  changeMemberRole(id: string, userId: string, role: ProjectRole): Observable<ProjectMemberDto> {
    return this.api.put<ProjectMemberDto>(`/projects/${id}/members/${userId}`, { role });
  }

  removeMember(id: string, userId: string): Observable<void> {
    return this.api.delete(`/projects/${id}/members/${userId}`);
  }

  /** Up to 20 active users who aren't in the project, matching the search in the email or name. */
  candidates(id: string, search: string): Observable<MemberCandidateDto[]> {
    const term = search.trim();
    return this.api.get<MemberCandidateDto[]>(`/projects/${id}/member-candidates`, {
      params: term === '' ? {} : { search: term },
    });
  }
}
