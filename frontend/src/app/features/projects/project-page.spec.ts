import { HttpErrorResponse } from '@angular/common/http';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Observable, of, throwError } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { SEARCH_DELAY_MS } from './member-sheet';
import {
  MemberCandidateDto,
  ProjectChanges,
  ProjectDto,
  ProjectMemberDto,
  ProjectRole,
} from './models';
import { ProjectsApi } from './projects-api';
import { PROJECTS_ROUTES } from './projects.routes';

function member(
  userId: string,
  displayName: string,
  role: ProjectRole,
  isActive = true,
): ProjectMemberDto {
  return {
    userId,
    displayName,
    role,
    isActive,
    email: `${displayName.split(' ')[0].toLowerCase()}@kanabol.test`,
  };
}

function refused(status: number, code: string): Observable<never> {
  return throwError(
    () => new HttpErrorResponse({ status, error: { code }, url: '/api/projects/p1' }),
  );
}

class FakeProjectsApi {
  project: ProjectDto = {
    id: 'p1',
    name: 'Kanabol',
    key: 'KAN',
    description: 'Board-ul echipei',
    createdAt: '2026-11-04T08:00:00Z',
    archivedAt: null,
    myRole: 'Owner',
    canManage: true,
    columns: [],
  };
  memberList: ProjectMemberDto[] = [
    member('me', 'Florin Danci', 'Owner'),
    member('u2', 'Ana Pop', 'Member'),
    member('u3', 'Bob Ionescu', 'Member', false),
  ];
  candidateList: MemberCandidateDto[] = [
    { id: 'u4', displayName: 'Dan Marin', email: 'dan@kanabol.test' },
  ];

  get = vi.fn((id: string): Observable<ProjectDto> => of({ ...this.project, id }));
  members = vi.fn(() => of(this.memberList));
  update = vi.fn((_id: string, changes: ProjectChanges): Observable<ProjectDto> => {
    this.project = { ...this.project, ...changes };
    return of(this.project);
  });
  archive = vi.fn(() => {
    this.project = { ...this.project, archivedAt: '2026-11-06T08:00:00Z' };
    return of(undefined);
  });
  restore = vi.fn(() => {
    this.project = { ...this.project, archivedAt: null };
    return of(this.project);
  });
  candidates = vi.fn((_id: string, search: string) =>
    of(
      this.candidateList.filter((each) =>
        each.displayName.toLowerCase().includes(search.toLowerCase()),
      ),
    ),
  );
  addMember = vi.fn((_id: string, userId: string, role: ProjectRole) => {
    const added = member(userId, 'Dan Marin', role);
    this.memberList = [...this.memberList, added];
    return of(added);
  });
  changeMemberRole = vi.fn((_id: string, userId: string, role: ProjectRole) => {
    this.memberList = this.memberList.map((each) =>
      each.userId === userId ? { ...each, role } : each,
    );
    return of(this.memberList.find((each) => each.userId === userId)!);
  });
  removeMember = vi.fn((_id: string, userId: string) => {
    this.memberList = this.memberList.filter((each) => each.userId !== userId);
    return of(undefined);
  });
}

describe('ProjectPage', () => {
  let api: FakeProjectsApi;
  let permissions: Set<string>;
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmation: { ask: ReturnType<typeof vi.fn>; discardChanges: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;
  let everywhere: HarnessLoader;

  beforeEach(() => {
    api = new FakeProjectsApi();
    permissions = new Set();
    snackbar = { show: vi.fn(), error: vi.fn() };
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'projects', children: PROJECTS_ROUTES }]),
        { provide: ProjectsApi, useValue: api },
        { provide: Snackbar, useValue: snackbar },
        { provide: Confirmation, useValue: confirmation },
        {
          provide: AuthStore,
          useValue: {
            user: signal({ id: 'me' }),
            has: (permission: string) => permissions.has(permission),
          },
        },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  afterEach(() => {
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  async function visit(url: string): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create(url);
    everywhere = TestbedHarnessEnvironment.documentRootLoader(harness.fixture);
    await settle();
    return page();
  }

  function page(): HTMLElement {
    return harness.routeNativeElement!;
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await harness.fixture.whenStable();
      harness.fixture.detectChanges();
    }
  }

  function text(): string {
    return page().textContent.replace(/\s+/g, ' ');
  }

  function segments(): string[] {
    return [...page().querySelectorAll('kb-segmented-nav a')].map((each) =>
      each.textContent.trim(),
    );
  }

  function button(text: string, within: ParentNode = page()): HTMLButtonElement | undefined {
    return [...within.querySelectorAll('button')].find((each) => each.textContent.includes(text));
  }

  function row(name: string): HTMLTableRowElement {
    const found = [...page().querySelectorAll<HTMLTableRowElement>('tr.kb-table-row')].find(
      (each) => each.querySelector('.name')?.textContent.includes(name),
    );
    if (!found) {
      throw new Error(`No row for ${name}`);
    }
    return found;
  }

  async function choose(name: string, action: string): Promise<void> {
    const menu = await everywhere.getHarness(
      MatMenuHarness.with({ selector: `[aria-label="Acțiuni pentru ${name}"]` }),
    );
    await menu.open();
    for (const item of await menu.getItems()) {
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

  describe('the header', () => {
    it('names the project, with the way back, and the tab after it', async () => {
      await visit('/projects/p1/members');

      expect(page().querySelector('h1')?.textContent.trim()).toBe('Kanabol');
      expect(page().querySelector('kb-page-header nav')?.textContent).toContain('Proiecte');
      expect(TestBed.inject(Title).getTitle()).toBe('Kanabol · Kanabol');
    });

    it('reads the project once, and its members once, however the signals settle', async () => {
      await visit('/projects/p1/members');
      await settle();

      expect(api.get).toHaveBeenCalledTimes(1);
      expect(api.members).toHaveBeenCalledTimes(1);
    });

    it('reads the next project when the address changes to it', async () => {
      await visit('/projects/p1/members');

      await harness.navigateByUrl('/projects/p2/members');
      await settle();

      expect(api.get.mock.calls.map((call) => call[0])).toEqual(['p1', 'p2']);
    });

    it('opens on the board', async () => {
      await visit('/projects/p1');

      expect(page().querySelector('kb-segmented-nav a.active')?.textContent.trim()).toBe('Board');
      expect(text()).toContain('Pagina e în lucru');
    });

    it('gives an owner every view, the reports and the settings among them', async () => {
      await visit('/projects/p1/members');

      expect(segments()).toEqual(['Board', 'Listă', 'Membri', 'Rapoarte', 'Setări']);
      expect(
        page().querySelector('kb-segmented-nav a[aria-current="page"]')?.textContent.trim(),
      ).toBe('Membri');
    });

    it('gives a member the board, the list and the members', async () => {
      api.project = { ...api.project, myRole: 'Member', canManage: false };
      await visit('/projects/p1/members');

      expect(segments()).toEqual(['Board', 'Listă', 'Membri']);
    });

    it('gives a member with reports.view the reports too, but not the settings', async () => {
      api.project = { ...api.project, myRole: 'Member', canManage: false };
      permissions.add('reports.view');
      await visit('/projects/p1/members');

      expect(segments()).toEqual(['Board', 'Listă', 'Membri', 'Rapoarte']);
    });

    it('says an archived project is read only, and where an owner restores it', async () => {
      api.project = { ...api.project, archivedAt: '2026-11-05T08:00:00Z' };
      await visit('/projects/p1/members');

      expect(page().querySelector('kb-banner')?.textContent).toContain('Acest proiect e arhivat');
      expect(page().querySelector('kb-banner')?.textContent).toContain('din Setări');
    });

    it('says when the project does not exist', async () => {
      api.get.mockReturnValueOnce(refused(404, 'Project.NotFound'));
      await visit('/projects/p1');

      expect(text()).toContain('Proiectul nu există');
      expect(page().querySelector('kb-segmented-nav')).toBeNull();
    });

    it('says when the project is closed to the user, and who can open it', async () => {
      api.get.mockReturnValueOnce(refused(403, 'Project.MemberRequired'));
      await visit('/projects/p1');

      expect(text()).toContain('Nu ai acces la acest proiect');
      expect(text()).toContain('Cere unui owner');
    });
  });

  describe('the members', () => {
    it('lists them with their role and whether their account is active', async () => {
      await visit('/projects/p1/members');

      expect(row('Florin Danci').querySelector('kb-chip.owner')?.textContent.trim()).toBe('Owner');
      expect(row('Florin Danci').textContent).toContain('(tu)');
      expect(row('Ana Pop').querySelector('kb-chip.member')?.textContent.trim()).toBe('Membru');
      expect(row('Ana Pop').querySelector('td.state')?.textContent.trim()).toBe('Activ');
      expect(row('Bob Ionescu').querySelector('kb-chip.inactive')?.textContent.trim()).toBe(
        'Dezactivat',
      );
    });

    it('gives the last owner no menu, since they can neither leave nor stop being owner', async () => {
      await visit('/projects/p1/members');

      expect(row('Florin Danci').querySelector('.kb-row-actions')).toBeNull();
      expect(row('Ana Pop').querySelector('.kb-row-actions')).not.toBeNull();
    });

    it('gives the last active owner no menu, even beside a deactivated owner', async () => {
      api.memberList = [
        member('me', 'Florin Danci', 'Owner'),
        member('u3', 'Bob Ionescu', 'Owner', false),
        member('u2', 'Ana Pop', 'Member'),
      ];
      await visit('/projects/p1/members');

      expect(row('Florin Danci').querySelector('.kb-row-actions')).toBeNull();
      expect(row('Bob Ionescu').querySelector('.kb-row-actions')).not.toBeNull();
    });

    it('gives a deactivated owner no menu when no owner is active, until a new one is made', async () => {
      api.memberList = [
        member('u3', 'Bob Ionescu', 'Owner', false),
        member('u2', 'Ana Pop', 'Member'),
      ];
      api.project = { ...api.project, myRole: null };
      await visit('/projects/p1/members');

      expect(row('Bob Ionescu').querySelector('.kb-row-actions')).toBeNull();
      expect(row('Ana Pop').querySelector('.kb-row-actions')).not.toBeNull();
    });

    it('lets a member see the others, and change nothing', async () => {
      api.project = { ...api.project, myRole: 'Member', canManage: false };
      await visit('/projects/p1/members');

      expect(button('Adaugă membru')).toBeUndefined();
      expect(page().querySelector('.kb-row-actions')).toBeNull();
    });

    it('lets nobody change the members of an archived project', async () => {
      api.project = { ...api.project, archivedAt: '2026-11-05T08:00:00Z' };
      await visit('/projects/p1/members');

      expect(button('Adaugă membru')).toBeUndefined();
      expect(page().querySelector('.kb-row-actions')).toBeNull();
    });

    it('makes a member an owner, after which the first owner may step down', async () => {
      await visit('/projects/p1/members');

      await choose('Ana Pop', 'Fă-l owner');

      expect(api.changeMemberRole).toHaveBeenCalledWith('p1', 'u2', 'Owner');
      expect(snackbar.show).toHaveBeenCalledWith('«Ana Pop» e acum owner.');
      expect(row('Florin Danci').querySelector('.kb-row-actions')).not.toBeNull();
    });

    it('removes a member after asking', async () => {
      await visit('/projects/p1/members');

      await choose('Ana Pop', 'Scoate din proiect');

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({
          title: 'Scoți «Ana Pop» din proiect?',
          confirmLabel: 'Scoate',
          destructive: true,
        }),
      );
      expect(api.removeMember).toHaveBeenCalledWith('p1', 'u2');
      expect(page().textContent).not.toContain('Ana Pop');
    });

    it('keeps the member when the question is answered no', async () => {
      confirmation.ask.mockResolvedValueOnce(false);
      await visit('/projects/p1/members');

      await choose('Ana Pop', 'Scoate din proiect');

      expect(api.removeMember).not.toHaveBeenCalled();
    });

    it('asks an owner who leaves in their own words', async () => {
      api.memberList = [member('me', 'Florin Danci', 'Owner'), member('u2', 'Ana Pop', 'Owner')];
      await visit('/projects/p1/members');
      const reads = api.get.mock.calls.length;

      await choose('Florin Danci', 'Ieși din proiect');

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Ieși din proiectul «Kanabol»?', confirmLabel: 'Ieși' }),
      );
      // Leaving may close the project to me, so it's read again to know.
      expect(api.get).toHaveBeenCalledTimes(reads + 1);
    });

    it('adds someone found by name, as the role chosen', async () => {
      await visit('/projects/p1/members');
      button('Adaugă membru')!.click();
      await settle();

      const people = await everywhere.getHarness(MatAutocompleteHarness);
      await people.enterText('dan');
      await new Promise((resolve) => setTimeout(resolve, SEARCH_DELAY_MS + 50));
      await settle();
      expect(api.candidates).toHaveBeenLastCalledWith('p1', 'dan');
      await people.selectOption({ text: /Dan Marin/ });
      const role = await everywhere.getHarness(MatSelectHarness.with({ selector: '#member-role' }));
      await role.clickOptions({ text: 'Owner' });
      button('Adaugă', sheet())!.click();
      await settle();

      expect(api.addMember).toHaveBeenCalledWith('p1', 'u4', 'Owner');
      expect(snackbar.show).toHaveBeenCalledWith('«Dan Marin» a fost adăugat în proiect.');
      expect(row('Dan Marin')).toBeDefined();
    });

    it('asks for someone from the list before adding', async () => {
      await visit('/projects/p1/members');
      button('Adaugă membru')!.click();
      await settle();

      button('Adaugă', sheet())!.click();
      await settle();

      expect(api.addMember).not.toHaveBeenCalled();
      expect(sheet().textContent).toContain('Alege un utilizator din listă.');
    });
  });

  describe('the settings', () => {
    function input(id: string): HTMLInputElement {
      return page().querySelector<HTMLInputElement>(`#${id}`)!;
    }

    it('show the key, which never changes, next to the name and the description', async () => {
      await visit('/projects/p1/settings');

      expect(input('settings-name').value).toBe('Kanabol');
      expect(input('settings-description').value).toBe('Board-ul echipei');
      expect(page().querySelector('.key')?.textContent.trim()).toBe('KAN');
      expect(page().querySelector('#settings-key')).toBeNull();
    });

    it('save the name and the description once they change', async () => {
      await visit('/projects/p1/settings');
      expect(button('Salvează')!.disabled).toBe(true);

      input('settings-name').value = ' Kanabol v2 ';
      input('settings-name').dispatchEvent(new Event('input'));
      input('settings-description').value = '  ';
      input('settings-description').dispatchEvent(new Event('input'));
      await settle();
      button('Salvează')!.click();
      await settle();

      expect(api.update).toHaveBeenCalledWith('p1', { name: 'Kanabol v2', description: null });
      expect(snackbar.show).toHaveBeenCalledWith('Proiectul «Kanabol v2» a fost salvat.');
      expect(page().querySelector('h1')?.textContent.trim()).toBe('Kanabol v2');
    });

    it('refuse an empty name before sending', async () => {
      await visit('/projects/p1/settings');

      input('settings-name').value = ' ';
      input('settings-name').dispatchEvent(new Event('input'));
      await settle();
      button('Salvează')!.click();
      await settle();

      expect(api.update).not.toHaveBeenCalled();
      expect(text()).toContain('Scrie numele proiectului.');
    });

    it('archive the project after asking, and then offer to restore it', async () => {
      await visit('/projects/p1/settings');

      button('Arhivează proiectul')!.click();
      await settle();

      expect(confirmation.ask).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Arhivezi proiectul «Kanabol»?', destructive: true }),
      );
      expect(api.archive).toHaveBeenCalledWith('p1');
      expect(page().querySelector('kb-banner')?.textContent).toContain('Acest proiect e arhivat');
      expect(input('settings-name').readOnly).toBe(true);
      expect(button('Salvează')).toBeUndefined();

      button('Restaurează proiectul')!.click();
      await settle();

      expect(api.restore).toHaveBeenCalledWith('p1');
      expect(page().querySelector('kb-banner')).toBeNull();
      expect(snackbar.show).toHaveBeenLastCalledWith('Proiectul «Kanabol» e din nou deschis.');
    });

    it('keep the project open when the question is answered no', async () => {
      confirmation.ask.mockResolvedValueOnce(false);
      await visit('/projects/p1/settings');

      button('Arhivează proiectul')!.click();
      await settle();

      expect(api.archive).not.toHaveBeenCalled();
    });
  });
});
