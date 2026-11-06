import { HttpErrorResponse } from '@angular/common/http';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatCheckboxHarness } from '@angular/material/checkbox/testing';
import { MatTooltipHarness } from '@angular/material/tooltip/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { PERMISSIONS } from '../../core/auth/permissions';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { RoleChanges, RoleDto } from './models';
import { RolesApi } from './roles-api';
import { RolesPage } from './roles-page';

const ALL = Object.values(PERMISSIONS);

const ADMIN: RoleDto = { id: 'r1', name: 'Admin', isSystem: true, permissions: ALL, userCount: 1 };
const LEAD: RoleDto = {
  id: 'r2',
  name: 'Lead',
  isSystem: false,
  permissions: ['tasks.create'],
  userCount: 3,
};
const GUEST: RoleDto = {
  id: 'r3',
  name: 'Oaspete',
  isSystem: false,
  permissions: [],
  userCount: 0,
};

class FakeRolesApi {
  roles = [ADMIN, LEAD, GUEST];
  list = vi.fn(() =>
    of({ items: this.roles, page: 1, pageSize: 100, totalCount: this.roles.length }),
  );
  permissions = vi.fn(() => of(ALL));
  create = vi.fn((changes: RoleChanges): Observable<RoleDto> =>
    of({ id: 'r9', isSystem: false, userCount: 0, ...changes }),
  );
  update = vi.fn((id: string, changes: RoleChanges): Observable<RoleDto> =>
    of({ ...this.roles.find((role) => role.id === id)!, ...changes }),
  );
  delete = vi.fn(() => of(null));
}

function refused(status: number, code: string): Observable<never> {
  return throwError(
    () => new HttpErrorResponse({ status, error: { status, code }, url: '/api/roles' }),
  );
}

describe('RolesPage', () => {
  let api: FakeRolesApi;
  let confirmation: {
    ask: ReturnType<typeof vi.fn>;
    discardChanges: ReturnType<typeof vi.fn>;
  };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<RolesPage>;
  let element: HTMLElement;
  let loader: HarnessLoader;

  beforeEach(() => {
    api = new FakeRolesApi();
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: RolesApi, useValue: api },
        { provide: Confirmation, useValue: confirmation },
        { provide: Snackbar, useValue: snackbar },
      ],
    });
  });

  async function open(): Promise<void> {
    fixture = TestBed.createComponent(RolesPage);
    element = fixture.nativeElement as HTMLElement;
    loader = TestbedHarnessEnvironment.loader(fixture);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  function items(): HTMLButtonElement[] {
    return [...element.querySelectorAll<HTMLButtonElement>('.role-list button')];
  }

  function item(name: string): HTMLButtonElement {
    const found = items().find((button) => button.querySelector('.name')?.textContent === name);
    if (!found) {
      throw new Error(`No role ${name} in the list`);
    }
    return found;
  }

  async function select(name: string): Promise<void> {
    item(name).click();
    await settle();
  }

  function button(label: string): HTMLButtonElement | undefined {
    return [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  async function click(label: string): Promise<void> {
    const found = button(label);
    if (!found) {
      throw new Error(`No button ${label}`);
    }
    found.click();
    await settle();
  }

  function nameField(): HTMLInputElement {
    return element.querySelector<HTMLInputElement>('input#role-name')!;
  }

  async function typeName(value: string): Promise<void> {
    nameField().value = value;
    nameField().dispatchEvent(new Event('input'));
    await settle();
  }

  async function checkbox(label: string): Promise<MatCheckboxHarness> {
    return loader.getHarness(MatCheckboxHarness.with({ label }));
  }

  it('lists the roles, with how many users each has and a lock on the system one', async () => {
    await open();

    expect(items().map((button) => button.querySelector('.name')?.textContent)).toEqual([
      'Admin',
      'Lead',
      'Oaspete',
    ]);
    expect(item('Admin').textContent).toContain('1 utilizator');
    expect(item('Lead').textContent).toContain('3 utilizatori');
    expect(item('Oaspete').textContent).toContain('niciun utilizator');
    expect(item('Admin').querySelector('.lock')).not.toBeNull();
    expect(item('Lead').querySelector('.lock')).toBeNull();
  });

  it('says how many users with "de" from 20 up, as Romanian does', async () => {
    api.roles = [{ ...LEAD, userCount: 25 }];
    await open();

    expect(item('Lead').textContent).toContain('25 de utilizatori');
  });

  it('opens on the first role, with its permissions ticked in their areas', async () => {
    await open();

    expect(item('Admin').getAttribute('aria-current')).toBe('true');
    const titles = [...element.querySelectorAll('legend')].map((legend) =>
      legend.textContent.trim(),
    );
    expect(titles).toEqual(['Utilizatori', 'Proiecte', 'Task-uri', 'Rapoarte']);
    const boxes = await loader.getAllHarnesses(MatCheckboxHarness);
    expect(boxes).toHaveLength(ALL.length);
  });

  it('shows the system role locked: nothing to tick, rename, save or delete, and says why', async () => {
    await open();

    for (const box of await loader.getAllHarnesses(MatCheckboxHarness)) {
      expect(await box.isChecked()).toBe(true);
      expect(await box.isDisabled()).toBe(true);
    }
    expect(nameField().disabled).toBe(true);
    expect(button('Salvează')).toBeUndefined();
    expect(button('Șterge rolul')).toBeUndefined();

    const tooltip = await loader.getHarness(MatTooltipHarness);
    await tooltip.show();
    expect(await tooltip.getTooltipText()).toBe('Rolul de sistem nu se poate modifica');
  });

  it('saves a role: its name and what is ticked, in the order the API lists them', async () => {
    await open();
    await select('Lead');

    expect(await (await checkbox('Creează task-uri')).isChecked()).toBe(true);
    await (await checkbox('Vede rapoartele')).check();
    await (await checkbox('Creează proiecte')).check();
    await typeName('Lead dev');
    await click('Salvează');

    expect(api.update).toHaveBeenCalledWith('r2', {
      name: 'Lead dev',
      permissions: ['projects.create', 'tasks.create', 'reports.view'],
    });
    expect(snackbar.show).toHaveBeenCalledWith('Rolul «Lead dev» a fost salvat.');
    expect(item('Lead dev').getAttribute('aria-current')).toBe('true');
  });

  it('puts the changes back with Anulează', async () => {
    await open();
    await select('Lead');
    await typeName('Altceva');
    await (await checkbox('Creează task-uri')).uncheck();

    await click('Anulează');

    expect(nameField().value).toBe('Lead');
    expect(await (await checkbox('Creează task-uri')).isChecked()).toBe(true);
  });

  it('creates a role from Rol nou, and selects it', async () => {
    await open();
    await click('Rol nou');

    expect(element.querySelector('.editor h2')?.textContent.trim()).toBe('Rol nou');
    expect(nameField().value).toBe('');
    expect(button('Șterge rolul')).toBeUndefined();
    await typeName('QA');
    await (await checkbox('Vede rapoartele')).check();
    await click('Salvează');

    expect(api.create).toHaveBeenCalledWith({ name: 'QA', permissions: ['reports.view'] });
    expect(snackbar.show).toHaveBeenCalledWith('Rolul «QA» a fost creat.');
    expect(item('QA').getAttribute('aria-current')).toBe('true');
  });

  it('asks for a name before sending anything', async () => {
    await open();
    await click('Rol nou');
    await typeName('   ');
    await click('Salvează');

    expect(api.create).not.toHaveBeenCalled();
    expect(element.querySelector('#role-name-error')?.textContent.trim()).toBe('Scrie un nume.');
    expect(nameField().getAttribute('aria-invalid')).toBe('true');

    await select('Lead');
    expect(element.querySelector('#role-name-error')).toBeNull();
  });

  it('shows a name already taken under the field', async () => {
    api.create.mockReturnValue(refused(409, 'Role.NameTaken'));
    await open();
    await click('Rol nou');
    await typeName('Lead');
    await click('Salvează');

    expect(element.querySelector('#role-name-error')?.textContent.trim()).toBe(
      'Există deja un rol cu acest nume.',
    );
    expect(snackbar.show).not.toHaveBeenCalled();
  });

  it('keeps the changes when the API refuses them', async () => {
    api.update.mockReturnValue(refused(403, 'Role.PermissionsBeyondYours'));
    await open();
    await select('Lead');
    await typeName('Lead dev');
    await click('Salvează');

    expect(nameField().value).toBe('Lead dev');
    expect(snackbar.show).not.toHaveBeenCalled();
    expect(item('Lead').getAttribute('aria-current')).toBe('true');
  });

  it('deletes a role after asking, red, with the verb', async () => {
    await open();
    await select('Lead');
    await click('Șterge rolul');

    expect(confirmation.ask).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Ștergi rolul «Lead»?',
        confirmLabel: 'Șterge',
        destructive: true,
      }),
    );
    expect(api.delete).toHaveBeenCalledWith('r2');
    expect(items().map((button) => button.querySelector('.name')?.textContent)).toEqual([
      'Admin',
      'Oaspete',
    ]);
    expect(snackbar.show).toHaveBeenCalledWith('Rolul «Lead» a fost șters.');
    expect(element.querySelector('.editor')).toBeNull();
  });

  it('deletes nothing when the answer is no', async () => {
    confirmation.ask.mockResolvedValue(false);
    await open();
    await select('Lead');
    await click('Șterge rolul');

    expect(api.delete).not.toHaveBeenCalled();
    expect(item('Lead')).toBeDefined();
  });

  it('asks before leaving changes behind, and stays when the answer is no', async () => {
    await open();
    await select('Lead');
    await typeName('Lead dev');

    confirmation.discardChanges.mockResolvedValue(false);
    await select('Oaspete');
    expect(confirmation.discardChanges).toHaveBeenCalled();
    expect(item('Lead').getAttribute('aria-current')).toBe('true');
    expect(nameField().value).toBe('Lead dev');

    confirmation.discardChanges.mockResolvedValue(true);
    await select('Oaspete');
    expect(item('Oaspete').getAttribute('aria-current')).toBe('true');
    expect(nameField().value).toBe('Oaspete');
  });

  it('switches without asking when nothing changed', async () => {
    await open();
    await select('Lead');
    await select('Oaspete');

    expect(confirmation.discardChanges).not.toHaveBeenCalled();
    expect(item('Oaspete').getAttribute('aria-current')).toBe('true');
  });

  it('says when the roles could not be loaded, and tries again', async () => {
    api.list.mockReturnValueOnce(refused(500, 'Unexpected'));
    await open();

    expect(element.textContent).toContain('Nu am putut încărca rolurile.');
    await click('Reîncearcă');

    expect(api.list).toHaveBeenCalledTimes(2);
    expect(items()).toHaveLength(3);
  });
});
