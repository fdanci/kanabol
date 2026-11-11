import { PERMISSIONS, Permission } from '../auth/permissions';

export interface NavItem {
  label: string;
  icon: string;
  link: string;
  /** Shown only with this permission. */
  permission?: Permission;
}

export interface NavGroup {
  title: string;
  items: NavItem[];
}

/** The source list (docs/UI_UX_SPEC.md §5), with the fixed icon set of §12. */
export function navigation(): NavGroup[] {
  return [
    {
      title: $localize`Lucru`,
      items: [
        { label: $localize`Panou principal`, icon: 'dashboard', link: '/' },
        { label: $localize`Proiectele mele`, icon: 'folder', link: '/projects' },
        { label: $localize`Task-urile mele`, icon: 'task_alt', link: '/my-tasks' },
        { label: $localize`Notificări`, icon: 'notifications', link: '/notifications' },
      ],
    },
    {
      title: $localize`Administrare`,
      items: [
        {
          label: $localize`Utilizatori`,
          icon: 'group',
          link: '/users',
          permission: PERMISSIONS.usersManage,
        },
        {
          label: $localize`Roluri`,
          icon: 'admin_panel_settings',
          link: '/roles',
          permission: PERMISSIONS.rolesManage,
        },
        {
          label: $localize`Rapoarte`,
          icon: 'bar_chart',
          link: '/reports',
          permission: PERMISSIONS.reportsView,
        },
        {
          label: $localize`Setări generale`,
          icon: 'settings',
          link: '/settings',
          permission: PERMISSIONS.settingsManage,
        },
      ],
    },
  ];
}
