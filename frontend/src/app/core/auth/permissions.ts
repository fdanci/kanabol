/** The permissions the API knows (docs/SPEC.md §4.4). The UI checks these, never role names. */
export const PERMISSIONS = {
  usersManage: 'users.manage',
  rolesManage: 'roles.manage',
  projectsCreate: 'projects.create',
  projectsViewAll: 'projects.view_all',
  projectsManage: 'projects.manage',
  tasksCreate: 'tasks.create',
  tasksArchive: 'tasks.archive',
  reportsView: 'reports.view',
  settingsManage: 'settings.manage',
} as const;

export type Permission = (typeof PERMISSIONS)[keyof typeof PERMISSIONS];
