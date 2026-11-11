import { PERMISSIONS, Permission } from '../../core/auth/permissions';

export interface PermissionChoice {
  name: string;
  label: string;
}

export interface PermissionGroup {
  title: string;
  permissions: PermissionChoice[];
}

/** The areas of docs/UI_UX_SPEC.md §7.7, in order, with what each permission lets someone do. */
function areas(): { title: string; permissions: [Permission, string][] }[] {
  return [
    {
      title: $localize`Utilizatori`,
      permissions: [
        [PERMISSIONS.usersManage, $localize`Administrează utilizatorii`],
        [PERMISSIONS.rolesManage, $localize`Administrează rolurile`],
      ],
    },
    {
      title: $localize`Proiecte`,
      permissions: [
        [PERMISSIONS.projectsCreate, $localize`Creează proiecte`],
        [PERMISSIONS.projectsViewAll, $localize`Vede toate proiectele`],
        [PERMISSIONS.projectsManage, $localize`Administrează orice proiect`],
      ],
    },
    {
      title: $localize`Task-uri`,
      permissions: [
        [PERMISSIONS.tasksCreate, $localize`Creează task-uri`],
        [PERMISSIONS.tasksArchive, $localize`Arhivează și restaurează task-uri`],
      ],
    },
    {
      title: $localize`Rapoarte`,
      permissions: [[PERMISSIONS.reportsView, $localize`Vede rapoartele`]],
    },
    {
      title: $localize`Setări`,
      permissions: [[PERMISSIONS.settingsManage, $localize`Schimbă setările generale`]],
    },
  ];
}

/**
 * The permissions the API knows, grouped by area. One the UI doesn't know yet still shows, under
 * its own name in a last group, so a role never hides what it grants.
 */
export function permissionGroups(available: readonly string[]): PermissionGroup[] {
  const offered = new Set(available);
  const known = new Set<string>();
  const groups: PermissionGroup[] = [];

  for (const area of areas()) {
    const permissions = area.permissions
      .filter(([name]) => offered.has(name))
      .map(([name, label]) => ({ name, label }));
    area.permissions.forEach(([name]) => known.add(name));
    if (permissions.length > 0) {
      groups.push({ title: area.title, permissions });
    }
  }

  const others = available.filter((name) => !known.has(name));
  if (others.length > 0) {
    groups.push({
      title: $localize`Altele`,
      permissions: others.map((name) => ({ name, label: name })),
    });
  }
  return groups;
}
