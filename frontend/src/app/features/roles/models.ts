/** A role, as GET /api/roles answers (docs/SPEC.md §4.4). */
export interface RoleDto {
  id: string;
  name: string;
  /** Admin: it can't be renamed, deleted or lose a permission. */
  isSystem: boolean;
  permissions: string[];
  /** Who has the role, deactivated users included. */
  userCount: number;
}

/** What creating or editing a role sends. */
export interface RoleChanges {
  name: string;
  permissions: string[];
}
