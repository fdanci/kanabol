import { HttpErrorResponse } from '@angular/common/http';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { NewProject, ProjectDto, ProjectSummaryDto } from './models';
import { ProjectsApi } from './projects-api';
import { ProjectsPage } from './projects-page';

const HOUR = 3_600_000;

function summary(
  id: string,
  name: string,
  extra: Partial<ProjectSummaryDto> = {},
): ProjectSummaryDto {
  return {
    id,
    name,
    key: name.slice(0, 3).toUpperCase(),
    myRole: 'Owner',
    owners: [{ userId: 'u1', displayName: 'Florin Danci' }],
    memberPreview: [{ userId: 'u1', displayName: 'Florin Danci' }],
    memberCount: 1,
    openTaskCount: 0,
    overdueTaskCount: 0,
    completedTaskCount: 0,
    updatedAt: new Date(Date.now() - 2 * HOUR).toISOString(),
    archivedAt: null,
    ...extra,
  };
}

function created(project: NewProject): ProjectDto {
  return {
    id: 'p9',
    ...project,
    createdAt: '2026-11-06T08:00:00Z',
    archivedAt: null,
    myRole: 'Owner',
    canManage: true,
    columns: [],
  };
}

class FakeProjectsApi {
  projects: ProjectSummaryDto[] = [
    summary('p1', 'Kanabol', {
      key: 'KAN',
      owners: [
        { userId: 'u1', displayName: 'Florin Danci' },
        { userId: 'u2', displayName: 'Ana Pop' },
      ],
      memberPreview: [
        { userId: 'u1', displayName: 'Florin Danci' },
        { userId: 'u2', displayName: 'Ana Pop' },
        { userId: 'u3', displayName: 'Bob Ionescu' },
        { userId: 'u4', displayName: 'Cora Marin' },
      ],
      memberCount: 7,
      openTaskCount: 12,
      overdueTaskCount: 3,
    }),
    summary('p2', 'Site', { key: 'WEB', archivedAt: '2026-11-01T08:00:00Z' }),
  ];
  totalCount = 2;

  list = vi.fn((page: number, pageSize: number) =>
    of({ items: this.projects, page, pageSize, totalCount: this.totalCount }),
  );
  create = vi.fn((project: NewProject): Observable<ProjectDto> => of(created(project)));
}

describe('ProjectsPage', () => {
  let api: FakeProjectsApi;
  let permissions: Set<string>;
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let confirmation: { ask: ReturnType<typeof vi.fn>; discardChanges: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<ProjectsPage>;
  let element: HTMLElement;
  let loader: HarnessLoader;

  beforeEach(() => {
    api = new FakeProjectsApi();
    permissions = new Set(['projects.create']);
    snackbar = { show: vi.fn(), error: vi.fn() };
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ProjectsApi, useValue: api },
        { provide: Snackbar, useValue: snackbar },
        { provide: Confirmation, useValue: confirmation },
        {
          provide: AuthStore,
          useValue: {
            user: signal({ id: 'u1' }),
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

  async function open(): Promise<void> {
    fixture = TestBed.createComponent(ProjectsPage);
    element = fixture.nativeElement as HTMLElement;
    loader = TestbedHarnessEnvironment.loader(fixture);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      fixture.detectChanges();
    }
  }

  function row(name: string): HTMLTableRowElement {
    const found = [...element.querySelectorAll<HTMLTableRowElement>('tr.kb-table-row')].find(
      (each) => each.querySelector('.name')?.textContent.includes(name),
    );
    if (!found) {
      throw new Error(`No row for ${name}`);
    }
    return found;
  }

  function cell(name: string, column: string): string {
    return row(name).querySelector(`td.${column}`)?.textContent.replace(/\s+/g, ' ').trim() ?? '';
  }

  function button(text: string): HTMLButtonElement | undefined {
    return [...element.querySelectorAll('button')].find((each) => each.textContent.includes(text));
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

  async function send(): Promise<void> {
    sheet().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle();
  }

  it('lists each project with its key, owners, members, tasks and last change', async () => {
    await open();

    expect(cell('Kanabol', 'name')).toContain('KAN');
    expect(cell('Kanabol', 'owners')).toBe('Florin Danci, Ana Pop');
    expect(row('Kanabol').querySelectorAll('td.members kb-avatar')).toHaveLength(4);
    expect(cell('Kanabol', 'members')).toContain('+3');
    expect(
      row('Kanabol').querySelector('td.members [role="img"]')?.getAttribute('aria-label'),
    ).toBe('7 membri');
    expect(cell('Kanabol', 'open')).toBe('12');
    expect(cell('Kanabol', 'overdue')).toBe('3');
    expect(row('Kanabol').querySelector('td.overdue')?.classList).toContain('late');
    expect(cell('Kanabol', 'updated')).toBe('acum 2 ore');
  });

  it('marks an archived project with the Arhivat chip', async () => {
    await open();

    expect(row('Site').querySelector('kb-chip.archived')?.textContent.trim()).toBe('Arhivat');
    expect(row('Kanabol').querySelector('kb-chip.archived')).toBeNull();
  });

  it('opens the board from the name, and from anywhere on the row', async () => {
    await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect(row('Kanabol').querySelector('a')?.getAttribute('href')).toBe('/projects/p1/board');
    row('Kanabol').querySelector<HTMLElement>('td.owners')!.click();

    expect(navigate).toHaveBeenCalledWith(['/projects', 'p1', 'board']);
  });

  it('shows the archived ones too when asked, from the first page', async () => {
    await open();
    const filter = element.querySelector<HTMLButtonElement>('.kb-filter-chip')!;
    expect(filter.getAttribute('aria-pressed')).toBe('false');

    filter.click();
    await settle();

    expect(api.list).toHaveBeenLastCalledWith(1, 25, true);
    expect(filter.getAttribute('aria-pressed')).toBe('true');
  });

  it('turns the pages, 25, 50 or 100 at a time', async () => {
    api.totalCount = 60;
    await open();

    const paginator = await loader.getHarness(MatPaginatorHarness);
    // The page provides the Romanian labels itself, so the paginator stays out of the first bundle.
    expect(await paginator.getRangeLabel()).toBe('1 – 25 din 60');
    await paginator.goToNextPage();
    await settle();

    expect(api.list).toHaveBeenLastCalledWith(2, 25, false);
  });

  it('offers a new project only with projects.create', async () => {
    permissions.clear();
    await open();

    expect(button('Proiect nou')).toBeUndefined();
  });

  it('says how a list with nothing in it fills, with the action for those who may take it', async () => {
    api.projects = [];
    api.totalCount = 0;
    await open();
    expect(element.querySelector('kb-empty-state')?.textContent).toContain('Niciun proiect încă');
    expect(button('Creează primul proiect')).toBeDefined();
  });

  it('tells someone who may not create projects how they get one', async () => {
    api.projects = [];
    api.totalCount = 0;
    permissions.clear();
    await open();

    expect(element.querySelector('kb-empty-state')?.textContent).toContain(
      'Când te adaugă cineva într-un proiect',
    );
    expect(button('Creează primul proiect')).toBeUndefined();
  });

  it('offers to try again when the list could not be read', async () => {
    api.list.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    await open();

    expect(element.textContent).toContain('Nu am putut încărca proiectele.');
    button('Reîncearcă')!.click();
    await settle();
    expect(cell('Kanabol', 'open')).toBe('12');
  });

  it('suggests a key from the name until one is typed, in capitals only', async () => {
    await open();
    button('Proiect nou')!.click();
    await settle();

    await type('project-name', 'Șantier nou');
    expect(field('project-key').value).toBe('SN');

    await type('project-key', 'kb-1x');
    expect(field('project-key').value).toBe('KBX');

    await type('project-name', 'Altceva');
    expect(field('project-key').value).toBe('KBX');
  });

  it('refuses a key that is not 2 to 5 letters before sending', async () => {
    await open();
    button('Proiect nou')!.click();
    await settle();
    await type('project-name', 'Kanabol');
    await type('project-key', 'K');

    await send();

    expect(api.create).not.toHaveBeenCalled();
    expect(sheet().textContent).toContain('Cheia are 2–5 litere, de la A la Z.');
  });

  it('creates the project, says so, and opens it', async () => {
    await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    button('Proiect nou')!.click();
    await settle();
    await type('project-name', ' Kanabol ');
    await type('project-description', 'Board-ul echipei');

    await send();

    expect(api.create).toHaveBeenCalledWith({
      name: 'Kanabol',
      key: 'KAN',
      description: 'Board-ul echipei',
    });
    expect(snackbar.show).toHaveBeenCalledWith('Proiectul «Kanabol» a fost creat.');
    expect(navigate).toHaveBeenCalledWith(['/projects', 'p9']);
    expect(document.querySelector('mat-dialog-container')).toBeNull();
  });

  it('puts a taken key under its field and keeps the sheet open', async () => {
    api.create.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { code: 'Project.KeyTaken' },
            url: '/api/projects',
          }),
      ),
    );
    await open();
    button('Proiect nou')!.click();
    await settle();
    await type('project-name', 'Kanabol');

    await send();

    expect(sheet().querySelector('#project-key-error')?.textContent).toContain(
      'Există deja un proiect cu această cheie',
    );
    expect(field('project-key').getAttribute('aria-invalid')).toBe('true');
  });
});
