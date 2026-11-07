import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HarnessLoader, TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { openTaskPanel } from '../../shared/task-panel';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskItemDto } from './models';
import { TaskPanelRoute } from './task-panel-route';
import { aMember, aProject, aTask, refused } from './task-testing';
import { TasksApi } from './tasks-api';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet /><router-outlet name="panel" />`,
})
class ShellLike {}

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `Pagina` })
class Page {}

describe('TaskPanel', () => {
  let task: TaskItemDto;
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let confirmation: { ask: ReturnType<typeof vi.fn>; discardChanges: ReturnType<typeof vi.fn> };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  const held = signal<string[]>([]);
  let harness: RouterTestingHarness;
  let overlay: HarnessLoader;

  function answer(changes: Partial<TaskItemDto>) {
    return vi.fn(() => {
      task = { ...task, ...changes, updatedAt: '2026-11-06T10:00:00Z' };
      return of(task);
    });
  }

  beforeEach(() => {
    task = aTask({ description: 'Cu **Azure AD** <script>alert(1)</script>' });
    held.set([]);
    api = {
      byKey: vi.fn(() => of(task)),
      project: vi.fn(() => of(aProject())),
      members: vi.fn(() => of([aMember('me', 'Florin Danci'), aMember('u2', 'Ana Pop')])),
      rename: vi.fn((_id: string, title: string) => answer({ title })()),
      describe: vi.fn((_id: string, description: string | null) => answer({ description })()),
      assign: vi.fn((_id: string, assigneeIds: string[]) =>
        answer({
          assignees: [
            { userId: 'me', displayName: 'Florin Danci' },
            { userId: 'u2', displayName: 'Ana Pop' },
          ].filter((person) => assigneeIds.includes(person.userId)),
        })(),
      ),
      prioritize: answer({ priority: 'High' }),
      schedule: vi.fn((_id: string, dueDate: string | null) => answer({ dueDate })()),
      move: answer({ columnId: 'c2', columnName: 'În lucru' }),
      archive: vi.fn(() => of(undefined)),
      restore: answer({ archivedAt: null }),
    };
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: ShellLike,
            children: [
              { path: 'page', component: Page },
              { path: 'task/:key', outlet: 'panel', component: TaskPanelRoute },
            ],
          },
        ]),
        { provide: TasksApi, useValue: api },
        { provide: AuthStore, useValue: { has: (name: string) => held().includes(name) } },
        { provide: Confirmation, useValue: confirmation },
        { provide: Snackbar, useValue: snackbar },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function open(url = '/page(panel:task/KAN-1)'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create(url);
    overlay = TestbedHarnessEnvironment.documentRootLoader(harness.fixture);
    await settle();
    return panel();
  }

  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      harness.fixture.detectChanges();
      await harness.fixture.whenStable();
    }
  }

  function panel(): HTMLElement {
    return document.querySelector<HTMLElement>('kb-task-panel')!;
  }

  function button(label: string): HTMLButtonElement | undefined {
    return [...panel().querySelectorAll('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  function select(labelId: string): Promise<MatSelectHarness> {
    return overlay.getHarness(
      MatSelectHarness.with({ selector: `[aria-labelledby~="${labelId}"]` }),
    );
  }

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
  });

  it('opens the task the address names, with its key, project and properties', async () => {
    const opened = await open();

    expect(api['byKey']).toHaveBeenCalledWith('KAN-1', false);
    expect(opened.querySelector('.key')?.textContent).toBe('KAN-1');
    expect(opened.querySelector('.context')?.textContent).toContain('Kanabol');
    expect(opened.querySelector('#kb-task-title')?.textContent.trim()).toBe('Login SSO');
    expect(await (await select('kb-task-column')).getValueText()).toBe('De făcut');
    expect(await (await select('kb-task-priority')).getValueText()).toContain('Medium');
    expect(await (await select('kb-task-assignee')).getValueText()).toBe('Neasignat');
  });

  // docs/SPEC.md §16: the description is markdown, sanitized, never raw HTML.
  it('renders the description as markdown, without the raw HTML in it', async () => {
    const opened = await open();

    expect(opened.querySelector('kb-markdown strong')?.textContent).toBe('Azure AD');
    expect(opened.querySelector('kb-markdown script')).toBeNull();
    expect(opened.querySelector('kb-markdown')?.textContent).toContain('alert(1)');
  });

  it('edits the title in place: Enter saves it', async () => {
    const opened = await open();

    opened.querySelector<HTMLButtonElement>('.title-button')!.click();
    await settle();
    const input = opened.querySelector<HTMLInputElement>('.title-field')!;
    input.value = 'Login cu SSO';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();

    expect(api['rename']).toHaveBeenCalledWith('t1', 'Login cu SSO');
    expect(opened.querySelector('#kb-task-title')?.textContent.trim()).toBe('Login cu SSO');
  });

  it('goes back to the title on Esc, without saving or closing the panel', async () => {
    const opened = await open();

    opened.querySelector<HTMLButtonElement>('.title-button')!.click();
    await settle();
    const input = opened.querySelector<HTMLInputElement>('.title-field')!;
    input.value = 'Altceva';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(api['rename']).not.toHaveBeenCalled();
    expect(panel()).not.toBeNull();
    expect(opened.querySelector('#kb-task-title')?.textContent.trim()).toBe('Login SSO');
  });

  it('refuses a blank title under the field, without sending it', async () => {
    const opened = await open();

    opened.querySelector<HTMLButtonElement>('.title-button')!.click();
    await settle();
    const input = opened.querySelector<HTMLInputElement>('.title-field')!;
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();

    expect(api['rename']).not.toHaveBeenCalled();
    expect(opened.querySelector('.kb-field-error')?.textContent).toBe('Scrie titlul task-ului.');
  });

  it('edits the description, and saves it', async () => {
    const opened = await open();

    button('Editează')!.click();
    await settle();
    const area = opened.querySelector<HTMLTextAreaElement>('.description-field')!;
    expect(area.value).toBe('Cu **Azure AD** <script>alert(1)</script>');
    area.value = '- unu\n- doi';
    area.dispatchEvent(new Event('input'));
    button('Salvează')!.click();
    await settle();

    expect(api['describe']).toHaveBeenCalledWith('t1', '- unu\n- doi');
    expect([...opened.querySelectorAll('kb-markdown li')].map((item) => item.textContent)).toEqual([
      'unu',
      'doi',
    ]);
  });

  it('changes the priority and the column where they are', async () => {
    await open();

    await (await select('kb-task-priority')).clickOptions({ text: /High/ });
    await (await select('kb-task-column')).clickOptions({ text: 'În lucru' });

    expect(api['prioritize']).toHaveBeenCalledWith('t1', 'High');
    expect(api['move']).toHaveBeenCalledWith('t1', 'c2');
  });

  // docs/UI_UX_SPEC.md §7.5: ticks with avatars, saved once, when the list closes.
  it('saves the assignees once, when their list closes, and shows them with their names', async () => {
    await open();

    const assignees = await select('kb-task-assignee');
    expect(await assignees.isMultiple()).toBe(true);
    await assignees.clickOptions({ text: /Ana Pop/ });
    await assignees.clickOptions({ text: /Florin Danci/ });
    expect(api['assign']).not.toHaveBeenCalled();

    await closeList(assignees);
    await settle();

    expect(api['assign']).toHaveBeenCalledTimes(1);
    expect(api['assign']).toHaveBeenCalledWith('t1', expect.arrayContaining(['me', 'u2']));
    expect((api['assign'].mock.calls[0] as [string, string[]])[1]).toHaveLength(2);
    const shown = panel().querySelector('.assignees .people');
    expect([...(shown?.querySelectorAll('kb-avatar') ?? [])].length).toBe(2);
    expect(shown?.textContent).toContain('Florin Danci');
    expect(shown?.textContent).toContain('Ana Pop');
  });

  it('sends nothing when the list closes as it was', async () => {
    await open();

    const assignees = await select('kb-task-assignee');
    await assignees.clickOptions({ text: /Ana Pop/ });
    await assignees.clickOptions({ text: /Ana Pop/ });
    await closeList(assignees);
    await settle();

    expect(api['assign']).not.toHaveBeenCalled();
  });

  it('puts the assignees back when the API refuses them, and says why', async () => {
    await open();
    api['assign'].mockReturnValueOnce(refused(400, 'Validation', { assigneeIds: ['No.'] }));

    const assignees = await select('kb-task-assignee');
    await assignees.clickOptions({ text: /Ana Pop/ });
    await closeList(assignees);
    await settle();

    expect(await assignees.getValueText()).toBe('Neasignat');
    expect(snackbar.error).toHaveBeenCalledWith(
      'Pot fi asignați doar membri activi ai proiectului, fiecare o dată.',
      null,
    );
  });

  // docs/SPEC.md §4.4: someone deactivated stays until someone takes them off, then can't come back.
  it('lets a deactivated assignee be taken off, but not given the task again', async () => {
    task = { ...task, assignees: [{ userId: 'u3', displayName: 'Bob Ionescu' }] };
    await open();

    const assignees = await select('kb-task-assignee');
    await assignees.open();
    const bob = (await assignees.getOptions({ text: /Bob Ionescu/ }))[0];
    expect(await bob.isSelected()).toBe(true);
    expect(await bob.isDisabled()).toBe(false);
    await bob.click();
    expect(await bob.isDisabled()).toBe(true);
    await closeList(assignees);
    await settle();

    expect(api['assign']).toHaveBeenCalledWith('t1', []);
  });

  it('shows the assignees with their names to whoever only reads the task', async () => {
    task = {
      ...task,
      canChange: false,
      assignees: [
        { userId: 'u2', displayName: 'Ana Pop' },
        { userId: 'me', displayName: 'Florin Danci' },
      ],
    };
    const opened = await open();

    const people = opened.querySelector('.assignees .people');
    expect(people?.querySelectorAll('kb-avatar').length).toBe(2);
    expect(people?.textContent).toContain('Ana Pop');
    expect(people?.textContent).toContain('Florin Danci');
    expect(opened.querySelector('#kb-task-assignee')?.textContent).toBe('Asignați');
  });

  it('clears the deadline', async () => {
    task = { ...task, dueDate: '2026-11-20T21:59:59.999Z' };
    const opened = await open();

    opened.querySelector<HTMLButtonElement>('button.clear')!.click();
    await settle();

    expect(api['schedule']).toHaveBeenCalledWith('t1', null);
  });

  it('shows the same task without controls to someone who can only read it', async () => {
    task = { ...task, canChange: false };
    const opened = await open();

    expect(opened.querySelector('.title-button')).toBeNull();
    expect(button('Editează')).toBeUndefined();
    expect(opened.querySelector('mat-select')).toBeNull();
    expect(opened.querySelector('.more')).toBeNull();
    expect(opened.querySelector('.properties')?.textContent).toContain('De făcut');
  });

  it('archives the task after asking, then closes', async () => {
    await open();

    await (
      await overlay.getHarness(MatMenuHarness.with({ selector: '.more' }))
    ).clickItem({
      text: /Arhivează/,
    });
    await settle();

    expect(confirmation.ask).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Arhivezi task-ul «Login SSO»?',
        confirmLabel: 'Arhivează',
      }),
    );
    expect(api['archive']).toHaveBeenCalledWith('t1');
    expect(snackbar.show).toHaveBeenCalledWith('Task-ul KAN-1 a fost arhivat.');
    expect(TestBed.inject(Router).url).toBe('/page');
  });

  it('offers to restore an archived task to tasks.archive', async () => {
    held.set(['tasks.archive']);
    task = { ...task, archivedAt: '2026-11-06T08:00:00Z', canChange: false };
    const opened = await open();

    expect(opened.querySelector('kb-banner')?.textContent).toContain('Acest task e arhivat');
    await (
      await overlay.getHarness(MatMenuHarness.with({ selector: '.more' }))
    ).clickItem({
      text: /Restaurează/,
    });
    await settle();

    expect(api['restore']).toHaveBeenCalledWith('t1');
  });

  it('says when the task is missing', async () => {
    api['byKey'].mockReturnValueOnce(refused(404, 'Task.NotFound'));

    const opened = await open('/page(panel:task/KAN-9)');

    expect(opened.textContent).toContain('Task-ul nu există');
  });

  it('closes the outlet when the panel closes, and keeps the page', async () => {
    await open();

    TestBed.inject(MatDialog).openDialogs[0].close();
    await settle();

    expect(TestBed.inject(Router).url).toBe('/page');
  });

  it('closes the panel when the address leaves it', async () => {
    await open();

    await TestBed.inject(Router).navigateByUrl('/page');
    await settle();

    expect(TestBed.inject(MatDialog).openDialogs.length).toBe(0);
  });

  it('opens from anywhere through openTaskPanel, keeping the page and its query', async () => {
    harness = await RouterTestingHarness.create('/page?sort=title:asc');

    await openTaskPanel(TestBed.inject(Router), 'KAN-1');
    await settle();

    expect(TestBed.inject(Router).url).toBe('/page(panel:task/KAN-1)?sort=title:asc');
    expect(panel()).not.toBeNull();
  });
});

/**
 * Closes a list as a person does, with Esc on its trigger. MatSelectHarness.close clicks the first
 * backdrop of the page, which in a sheet or the panel is the dialog's, and closes that instead.
 */
async function closeList(select: MatSelectHarness): Promise<void> {
  await (await select.host()).sendKeys(TestKey.ESCAPE);
}
