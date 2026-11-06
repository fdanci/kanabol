/** A user's role on a project (docs/SPEC.md §4.4), as the API writes it. */
export type ProjectRole = 'Owner' | 'Member';

export interface MemberSummaryDto {
  userId: string;
  displayName: string;
}

/** A row of GET /api/projects (docs/UI_UX_SPEC.md §7.3). */
export interface ProjectSummaryDto {
  id: string;
  name: string;
  key: string;
  /** Null for someone who sees it through projects.view_all or projects.manage alone. */
  myRole: ProjectRole | null;
  owners: MemberSummaryDto[];
  /** The first four members, owners first, for the avatars. */
  memberPreview: MemberSummaryDto[];
  memberCount: number;
  /** Outside a done column, archived tasks left out. */
  openTaskCount: number;
  /** Open past their deadline. */
  overdueTaskCount: number;
  /** The last change to the project or to one of its tasks. */
  updatedAt: string;
  archivedAt: string | null;
}

export interface BoardColumnDto {
  id: string;
  name: string;
  position: number;
  isDone: boolean;
}

/** GET /api/projects/{id}: the project as the person asking sees it. */
export interface ProjectDto {
  id: string;
  name: string;
  /** The prefix of the task keys, as in KAN-123. It never changes. */
  key: string;
  description: string | null;
  createdAt: string;
  archivedAt: string | null;
  myRole: ProjectRole | null;
  /** An owner, or projects.manage: the UI shows what they can change. The API checks it anyway. */
  canManage: boolean;
  columns: BoardColumnDto[];
}

export interface ProjectMemberDto {
  userId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
  /** A deactivated member keeps their place until an owner removes them. */
  isActive: boolean;
}

/** An active user who isn't in the project yet. */
export interface MemberCandidateDto {
  id: string;
  email: string;
  displayName: string;
}

export interface NewProject {
  name: string;
  key: string;
  description: string | null;
}

/** What editing a project sends: the key isn't there, since it never changes. */
export interface ProjectChanges {
  name: string;
  description: string | null;
}
