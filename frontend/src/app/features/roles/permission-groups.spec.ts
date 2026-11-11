import { PERMISSIONS } from '../../core/auth/permissions';
import { permissionGroups } from './permission-groups';

describe('permissionGroups', () => {
  const all = Object.values(PERMISSIONS);

  it('groups the permissions by area, in the order of UI spec 7.7', () => {
    const groups = permissionGroups(all);

    expect(groups.map((group) => group.title)).toEqual([
      'Utilizatori',
      'Proiecte',
      'Task-uri',
      'Rapoarte',
      'Setări',
    ]);
    expect(groups.map((group) => group.permissions.map((permission) => permission.name))).toEqual([
      ['users.manage', 'roles.manage'],
      ['projects.create', 'projects.view_all', 'projects.manage'],
      ['tasks.create', 'tasks.archive'],
      ['reports.view'],
      ['settings.manage'],
    ]);
  });

  it('names every permission in Romanian', () => {
    const labels = permissionGroups(all).flatMap((group) =>
      group.permissions.map((permission) => permission.label),
    );

    expect(labels).toHaveLength(all.length);
    for (const label of labels) {
      expect(label).not.toMatch(/\./);
    }
  });

  it('leaves out the areas the API has no permission for', () => {
    const groups = permissionGroups(['reports.view']);

    expect(groups.map((group) => group.title)).toEqual(['Rapoarte']);
  });

  it('keeps a permission it does not know, under its own name, rather than hide it', () => {
    const groups = permissionGroups(['users.manage', 'boards.export']);

    expect(groups.at(-1)).toEqual({
      title: 'Altele',
      permissions: [{ name: 'boards.export', label: 'boards.export' }],
    });
  });
});
