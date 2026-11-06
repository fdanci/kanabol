/** A user, as GET /api/users answers (docs/SPEC.md §4.4). Users are deactivated, never deleted. */
export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  roleId: string;
  roleName: string;
  isActive: boolean;
  /** After an account is made or its password reset, until the user picks their own. */
  mustChangePassword: boolean;
  /** Set after too many failed logins (docs/SPEC.md §4.5), and in the past once it's over. */
  lockedUntil: string | null;
  createdAt: string;
}

/** A role the caller may give: GET /api/users/assignable-roles. */
export interface AssignableRoleDto {
  id: string;
  name: string;
}

/** What creating a user sends. */
export interface NewUser {
  email: string;
  displayName: string;
  roleId: string;
  initialPassword: string;
}

/** What editing a user sends. The role and the password have endpoints of their own. */
export interface UserChanges {
  email: string;
  displayName: string;
}
