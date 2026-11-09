import { HttpErrorResponse } from '@angular/common/http';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { NewUser, UserChanges, UserDto } from './models';
import { UsersApi } from './users-api';
import { UsersPage } from './users-page';

const CREATED = '2026-11-04T08:00:00Z';

function user(id: string, displayName: string, extra: Partial<UserDto> = {}): UserDto {
  return {
    id,
    email: `${displayName.split(' ')[0].toLowerCase()}@kanabol.test`,
    displayName,
    roleId: 'r3',
    roleName: 'Membru',
    isActive: true,
    mustChangePassword: false,
    lockedUntil: null,
    createdAt: CREATED,
    ...extra,
  };
}

const ROLES = [
  { id: 'r1', name: 'Admin' },
  { id: 'r2', name: 'Lead' },
  { id: 'r3', name: 'Membru' },
];

class FakeUsersApi {
  users: UserDto[] = [
    user('u1', 'Florin Danci', { roleId: 'r1', roleName: 'Admin' }),
    // A lockout that is over already.
    user('u2', 'Ana Pop', { lockedUntil: '2026-01-01T08:00:00Z' }),
    user('u3', 'Bob Ionescu', { isActive: false }),
    user('u4', 'Cora Marin', {
      mustChangePassword: true,
      lockedUntil: new Date(Date.now() + 10 * 60_000).toISOString(),
    }),
  ];
  totalCount = 60;

  private find(id: string): UserDto {
    return this.users.find((each) => each.id === id)!;
  }

  list = vi.fn((page: number, pageSize: number) =>
    of({ items: this.users, page, pageSize, totalCount: this.totalCount }),
  );
  assignableRoles = vi.fn(() => of(ROLES));
  create = vi.fn((created: NewUser): Observable<UserDto> =>
    of(user('u9', created.displayName, { email: created.email, roleId: created.roleId })),
  );
  update = vi.fn((id: string, changes: UserChanges): Observable<UserDto> =>
    of({ ...this.find(id), ...changes }),
  );
  changeRole = vi.fn((id: string, roleId: string): Observable<UserDto> =>
    of({ ...this.find(id), roleId, roleName: ROLES.find((role) => role.id === roleId)!.name }),
  );
  deactivate = vi.fn((id: string): Observable<UserDto> =>
    of({ ...this.find(id), isActive: false }),
  );
  activate = vi.fn((id: string): Observable<UserDto> => of({ ...this.find(id), isActive: true }));
  resetPassword = vi.fn((id: string): Observable<UserDto> =>
    of({ ...this.find(id), mustChangePassword: true, lockedUntil: null }),
  );
}

function refused(status: number, body: object): Observable<never> {
  return throwError(() => new HttpErrorResponse({ status, error: body, url: '/api/users' }));
}

describe('UsersPage', () => {
  let api: FakeUsersApi;
  let confirmation: {
    ask: ReturnType<typeof vi.fn>;
    discardChanges: ReturnType<typeof vi.fn>;
  };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<UsersPage>;
  let element: HTMLElement;
  let loader: HarnessLoader;
  let everywhere: HarnessLoader;

  beforeEach(() => {
    api = new FakeUsersApi();
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: UsersApi, useValue: api },
        { provide: Confirmation, useValue: confirmation },
        { provide: Snackbar, useValue: snackbar },
        { provide: AuthStore, useValue: { user: signal({ id: 'u1' }) } },
        // Sheets close at once, instead of after their animation.
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  afterEach(() => {
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  async function open(): Promise<void> {
    fixture = TestBed.createComponent(UsersPage);
    element = fixture.nativeElement as HTMLElement;
    loader = TestbedHarnessEnvironment.loader(fixture);
    everywhere = TestbedHarnessEnvironment.documentRootLoader(fixture);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  function rows(): HTMLTableRowElement[] {
    return [...element.querySelectorAll<HTMLTableRowElement>('tr.kb-table-row')];
  }

  function row(name: string): HTMLTableRowElement {
    const found = rows().find((each) => each.querySelector('.name')?.textContent.includes(name));
    if (!found) {
      throw new Error(`No row for ${name}`);
    }
    return found;
  }

  function cell(name: string, column: string): string {
    return row(name).querySelector(`td.${column}`)?.textContent.replace(/\s+/g, ' ').trim() ?? '';
  }

  async function menuOf(name: string): Promise<MatMenuHarness> {
    return loader.getHarness(
      MatMenuHarness.with({ selector: `[aria-label="Acțiuni pentru ${name}"]` }),
    );
  }

  async function menuItems(name: string): Promise<string[]> {
    const menu = await menuOf(name);
    await menu.open();
    const labels = await Promise.all((await menu.getItems()).map((item) => item.getText()));
    await menu.close();
    // Without the icon's ligature in front: "editEditează".
    return labels.map((label) => label.replace(/^[a-z_]+/, '').trim());
  }

  async function choose(name: string, action: string): Promise<void> {
    const menu = await menuOf(name);
    await menu.open();
    const items = await menu.getItems();
    for (const item of items) {
      if ((await item.getText()).includes(action)) {
        await item.click();
        await settle();
        return;
      }
    }
    throw new Error(`No ${action} in the menu of ${name}`);
  }

  function sheet(): HTMLElement {
    const found = document.querySelector<HTMLElement>('mat-dialog-container');
    if (!found) {
      throw new Error('No sheet open');
    }
    return found;
  }

  function field(id: string): HTMLInputElement {
    return sheet().querySelector<HTMLInputElement>(`#${id}`)!;
  }

  async function type(id: string, value: string): Promise<void> {
    field(id).value = value;
    field(id).dispatchEvent(new Event('input'));
    await settle();
  }

  function errorOf(id: string): string | undefined {
    return sheet().querySelector(`#${id}-error`)?.textContent.trim();
  }

  async function press(label: string): Promise<void> {
    const button = [...sheet().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
    if (!button) {
      throw new Error(`No button ${label} in the sheet`);
    }
    button.click();
    await settle();
  }

  async function pickRole(name: string): Promise<void> {
    const select = await everywhere.getHarness(MatSelectHarness.with({ selector: '#user-role' }));
    await select.open();
    await select.clickOptions({ text: name });
    await settle();
  }

  it('lists the users: name, email, role, state and when the account was made', async () => {
    await open();

    expect(api.list).toHaveBeenCalledWith(1, 25);
    expect(rows()).toHaveLength(4);
    expect(cell('Ana Pop', 'email')).toBe('ana@kanabol.test');
    expect(cell('Ana Pop', 'role')).toBe('Membru');
    expect(cell('Ana Pop', 'created')).toBe('04.11.2026');
    expect(cell('Ana Pop', 'state')).toBe('Activ');
    expect(cell('Bob Ionescu', 'state')).toBe('Dezactivat');
    expect(cell('Cora Marin', 'state')).toContain('Blocat');
    expect(cell('Cora Marin', 'state')).toContain('Parolă de schimbat');
    expect(cell('Cora Marin', 'state')).not.toContain('Activ');
  });

  it('marks the row of whoever is signed in', async () => {
    await open();

    expect(cell('Florin Danci', 'name')).toContain('(tu)');
    expect(cell('Ana Pop', 'name')).not.toContain('(tu)');
  });

  it('pages through the users, 25, 50 or 100 at a time', async () => {
    await open();
    const paginator = await loader.getHarness(MatPaginatorHarness);

    expect(await paginator.getRangeLabel()).toBe('1 – 25 din 60');
    await paginator.goToNextPage();
    await settle();
    expect(api.list).toHaveBeenLastCalledWith(2, 25);

    await paginator.setPageSize(50);
    await settle();
    expect(api.list).toHaveBeenLastCalledWith(1, 50);
  });

  it('offers what each row allows: no reset or deactivation of your own account', async () => {
    await open();

    expect(await menuItems('Ana Pop')).toEqual([
      'Editează',
      'Schimbă rolul',
      'Resetează parola',
      'Dezactivează',
    ]);
    expect(await menuItems('Bob Ionescu')).toEqual([
      'Editează',
      'Schimbă rolul',
      'Resetează parola',
      'Activează',
    ]);
    expect(await menuItems('Florin Danci')).toEqual(['Editează', 'Schimbă rolul']);
  });

  it('creates a user in a sheet, with the roles the API says can be given', async () => {
    await open();
    element.querySelector<HTMLButtonElement>('kb-page-header button')!.click();
    await settle();

    await type('user-email', 'dan@kanabol.test');
    await type('user-name', 'Dan Rusu');
    await pickRole('Lead');
    await type('user-password', 'o parola initiala');
    await press('Creează');

    expect(api.create).toHaveBeenCalledWith({
      email: 'dan@kanabol.test',
      displayName: 'Dan Rusu',
      roleId: 'r2',
      initialPassword: 'o parola initiala',
    });
    expect(document.querySelector('mat-dialog-container')).toBeNull();
    expect(snackbar.show).toHaveBeenCalledWith('Contul lui «Dan Rusu» a fost creat.');
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('says what is missing before sending anything', async () => {
    await open();
    element.querySelector<HTMLButtonElement>('kb-page-header button')!.click();
    await settle();

    field('user-email').dispatchEvent(new Event('blur'));
    await settle();
    expect(errorOf('user-email')).toBe('Scrie adresa de email.');
    expect(errorOf('user-name')).toBeUndefined();

    await press('Creează');

    expect(api.create).not.toHaveBeenCalled();
    expect(errorOf('user-email')).toBe('Scrie adresa de email.');
    expect(errorOf('user-name')).toBe('Scrie numele.');
    expect(errorOf('user-role')).toBe('Alege un rol.');
    expect(errorOf('user-password')).toBe('Cel puțin 10 caractere.');

    await type('user-email', 'nu e o adresa');
    expect(errorOf('user-email')).toBe('Adresa de email nu e validă.');
  });

  it('shows an email already taken under its field, and keeps the sheet open', async () => {
    api.create.mockReturnValue(refused(409, { status: 409, code: 'User.EmailTaken' }));
    await open();
    element.querySelector<HTMLButtonElement>('kb-page-header button')!.click();
    await settle();
    await type('user-email', 'ana@kanabol.test');
    await type('user-name', 'Ana Pop');
    await pickRole('Membru');
    await type('user-password', 'o parola initiala');
    await press('Creează');

    expect(errorOf('user-email')).toBe('Există deja un utilizator cu această adresă.');
    expect(snackbar.show).not.toHaveBeenCalled();
    expect(sheet()).toBeDefined();

    await type('user-email', 'ana2@kanabol.test');
    expect(errorOf('user-email')).toBeUndefined();
  });

  it('edits the email and the name', async () => {
    await open();
    await choose('Ana Pop', 'Editează');

    expect(field('user-email').value).toBe('ana@kanabol.test');
    expect(field('user-role')).toBeNull();
    expect(field('user-password')).toBeNull();
    await type('user-name', 'Ana Maria Pop');
    await press('Salvează');

    expect(api.update).toHaveBeenCalledWith('u2', {
      email: 'ana@kanabol.test',
      displayName: 'Ana Maria Pop',
    });
    expect(cell('Ana Maria Pop', 'email')).toBe('ana@kanabol.test');
    expect(snackbar.show).toHaveBeenCalledWith('Contul lui «Ana Maria Pop» a fost salvat.');
  });

  it('changes the role, among the roles the API says can be given', async () => {
    await open();
    await choose('Ana Pop', 'Schimbă rolul');

    expect(api.assignableRoles).toHaveBeenCalled();
    await pickRole('Lead');
    await press('Schimbă rolul');

    expect(api.changeRole).toHaveBeenCalledWith('u2', 'r2');
    expect(cell('Ana Pop', 'role')).toBe('Lead');
    expect(snackbar.show).toHaveBeenCalledWith('«Ana Pop» are acum rolul Lead.');
  });

  it('deactivates after asking, red, with the verb', async () => {
    await open();
    await choose('Ana Pop', 'Dezactivează');

    expect(confirmation.ask).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Dezactivezi contul lui «Ana Pop»?',
        confirmLabel: 'Dezactivează',
        destructive: true,
      }),
    );
    expect(api.deactivate).toHaveBeenCalledWith('u2');
    expect(cell('Ana Pop', 'state')).toBe('Dezactivat');
    expect(snackbar.show).toHaveBeenCalledWith('Contul lui «Ana Pop» a fost dezactivat.');
  });

  it('deactivates nothing when the answer is no', async () => {
    confirmation.ask.mockResolvedValue(false);
    await open();
    await choose('Ana Pop', 'Dezactivează');

    expect(api.deactivate).not.toHaveBeenCalled();
    expect(cell('Ana Pop', 'state')).toBe('Activ');
  });

  it('activates without asking, since it can be undone', async () => {
    await open();
    await choose('Bob Ionescu', 'Activează');

    expect(confirmation.ask).not.toHaveBeenCalled();
    expect(api.activate).toHaveBeenCalledWith('u3');
    expect(cell('Bob Ionescu', 'state')).toBe('Activ');
    expect(snackbar.show).toHaveBeenCalledWith('Contul lui «Bob Ionescu» e activ din nou.');
  });

  it('resets a password to an initial one, which the user changes at the next sign-in', async () => {
    await open();
    await choose('Cora Marin', 'Resetează parola');

    await type('user-password', 'scurta');
    await press('Resetează parola');
    expect(api.resetPassword).not.toHaveBeenCalled();
    expect(errorOf('user-password')).toBe('Cel puțin 10 caractere.');

    await type('user-password', 'o parola noua de la admin');
    await press('Resetează parola');

    expect(api.resetPassword).toHaveBeenCalledWith('u4', 'o parola noua de la admin');
    expect(cell('Cora Marin', 'state')).not.toContain('Blocat');
    expect(snackbar.show).toHaveBeenCalledWith('Parola lui «Cora Marin» a fost resetată.');
  });

  it('shows a password the server finds too common under its field', async () => {
    api.resetPassword.mockReturnValue(
      refused(400, { status: 400, errors: { initialPassword: ['Too common.'] } }),
    );
    await open();
    await choose('Cora Marin', 'Resetează parola');
    await type('user-password', 'password123');
    await press('Resetează parola');

    expect(errorOf('user-password')).toBe('Parola e prea des folosită. Alege alta.');
  });

  it('asks before Esc throws away what was typed in a sheet', async () => {
    await open();
    await choose('Ana Pop', 'Editează');
    await type('user-name', 'Altcineva');

    confirmation.discardChanges.mockResolvedValue(false);
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(confirmation.discardChanges).toHaveBeenCalled();
    expect(sheet()).toBeDefined();

    confirmation.discardChanges.mockResolvedValue(true);
    await press('Anulează');
    expect(document.querySelector('mat-dialog-container')).toBeNull();
    expect(api.update).not.toHaveBeenCalled();
  });

  it('closes an untouched sheet with Esc without asking', async () => {
    await open();
    await choose('Ana Pop', 'Editează');

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(confirmation.discardChanges).not.toHaveBeenCalled();
    expect(document.querySelector('mat-dialog-container')).toBeNull();
  });

  it('says when the users could not be loaded, and tries again', async () => {
    api.list.mockReturnValueOnce(refused(500, { status: 500 }));
    await open();

    expect(element.textContent).toContain('Nu am putut încărca utilizatorii.');
    [...element.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent.trim() === 'Reîncearcă')!
      .click();
    await settle();

    expect(rows()).toHaveLength(4);
  });
});
