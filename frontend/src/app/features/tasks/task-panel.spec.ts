import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { HarnessLoader, TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { getDebugNode } from '@angular/core';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatTabHarness } from '@angular/material/tabs/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { openTaskPanel } from '../../shared/task-panel';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskItemDto } from './models';
import { TaskPanelRoute } from './task-panel-route';
import { CommentsApi } from './comments-api';
import {
  PANEL_COLUMNS,
  aComment,
  aMember,
  aProject,
  aTask,
  anAttachment,
  anEntry,
  refused,
} from './task-testing';
import { TasksApi } from './tasks-api';

import { MoveQuestions } from '../../shared/move-questions';
import { MatTooltip } from '@angular/material/tooltip';
import { MatTabGroupHarness } from '@angular/material/tabs/testing';
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
  let comments: Record<string, ReturnType<typeof vi.fn>>;
  let confirmation: { ask: ReturnType<typeof vi.fn>; discardChanges: ReturnType<typeof vi.fn> };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let questions: { estimate: ReturnType<typeof vi.fn>; pauseReason: ReturnType<typeof vi.fn> };
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
      history: vi.fn(() =>
        of({
          items: [anEntry({ oldValue: 'Login SSO', newValue: 'Login cu SSO' })],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      ),
    };
    comments = { list: vi.fn(() => of([aComment()])), add: vi.fn(), edit: vi.fn() };
    confirmation = {
      ask: vi.fn(() => Promise.resolve(true)),
      discardChanges: vi.fn(() => Promise.resolve(true)),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };
    // The board's sheets (docs/UI_UX_SPEC.md §7.5) answer at once: an hour, or the reason.
    questions = {
      estimate: vi.fn(() => Promise.resolve(60)),
      pauseReason: vi.fn(() => Promise.resolve('Aștept clientul')),
    };

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
        { provide: CommentsApi, useValue: comments },
        { provide: AuthStore, useValue: { has: (name: string) => held().includes(name) } },
        { provide: Confirmation, useValue: confirmation },
        { provide: Snackbar, useValue: snackbar },
        { provide: MoveQuestions, useValue: questions },
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

  /** What a sighted reader sees: the text without what's hidden from them, such as an icon's ligature. */
  function visibleText(element: HTMLElement): string {
    const copy = element.cloneNode(true) as HTMLElement;
    copy.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => {
      hidden.remove();
    });
    return copy.textContent.replace(/\s+/g, ' ').trim();
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
    expect(api['move']).toHaveBeenCalledWith('t1', 'c2', undefined, { estimatedMinutes: 60 });
  });

  // docs/UI_UX_SPEC.md §7.5: the select shows the priority's chip, through mat-select-trigger, not
  // the text of its option, which held the icon's name: "arrow_upwardHigh" (a bug from group 12).
  it('shows the priority as its chip in the select, not the name of its icon', async () => {
    await open();
    const host = panel().querySelector<HTMLElement>('[aria-labelledby~="kb-task-priority"]')!;
    expect(visibleText(host)).toBe('Medium');
    expect(host.querySelector('kb-chip')?.classList).toContain('medium');

    await (await select('kb-task-priority')).clickOptions({ text: /High/ });
    await settle();

    expect(visibleText(host)).toBe('High');
    expect(host.querySelector('kb-chip')?.classList).toContain('high');

    api['prioritize'].mockReturnValueOnce(refused(403, 'Project.MemberRequired'));
    await (await select('kb-task-priority')).clickOptions({ text: /Critical/ });
    await settle();

    expect(visibleText(host)).toBe('High');
    expect(host.querySelector('kb-chip')?.classList).toContain('high');
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

  // docs/UI_UX_SPEC.md §6.7 and §7.5: Comentarii first, then Atașamente and Istoric.
  it('shows the comments and the files of the task in its tabs', async () => {
    task = aTask({ attachments: [anAttachment({ fileName: 'brief.pdf' })] });
    const opened = await open();

    const tabs = await overlay.getAllHarnesses(MatTabHarness);
    expect(await Promise.all(tabs.map((tab) => tab.getLabel()))).toEqual([
      'Comentarii',
      'Atașamente',
      'Istoric',
    ]);
    expect(comments['list']).toHaveBeenCalledWith('t1');
    expect(opened.querySelector('kb-task-comments .comment')?.textContent).toContain('Merge');
    expect(opened.querySelector('kb-task-comments .new')).not.toBeNull();

    await tabs[1].select();
    await settle();

    expect(opened.querySelector('kb-task-attachments .attachment .name')?.textContent.trim()).toBe(
      'brief.pdf',
    );
    expect(opened.querySelector('kb-task-attachments kb-upload-zone')).not.toBeNull();
    expect(opened.textContent).not.toContain('vin într-o versiune următoare');
  });

  // docs/UI_UX_SPEC.md §7.5: the timeline is read when its tab opens, and again after each change
  // saved from the panel, without SignalR.
  it('reads the history when its tab opens, and again after a change saved in the panel', async () => {
    const opened = await open();
    expect(api['history']).not.toHaveBeenCalled();

    const tabs = await overlay.getAllHarnesses(MatTabHarness);
    await tabs[2].select();
    await settle();

    expect(api['history']).toHaveBeenCalledTimes(1);
    expect(api['history']).toHaveBeenCalledWith('t1', 1);
    expect(opened.querySelector('kb-task-timeline .entry')?.textContent).toContain('Login cu SSO');
    expect(opened.textContent).not.toContain('vine într-o versiune următoare');

    await (await select('kb-task-priority')).clickOptions({ text: /High/ });
    await settle();
    expect(api['history']).toHaveBeenCalledTimes(2);

    await tabs[0].select();
    await settle();
    await tabs[2].select();
    await settle();
    expect(api['history']).toHaveBeenCalledTimes(3);
  });

  it('has neither a comment form nor an upload zone for someone who can only read the task', async () => {
    task = aTask({ canChange: false, attachments: [anAttachment({ canDelete: false })] });
    const opened = await open();

    expect(opened.querySelector('kb-task-comments .new')).toBeNull();
    await (await overlay.getAllHarnesses(MatTabHarness))[1].select();
    await settle();
    expect(opened.querySelector('kb-task-attachments kb-upload-zone')).toBeNull();
    expect(opened.querySelector('kb-task-attachments .delete')).toBeNull();
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
  // docs/UI_UX_SPEC.md §7.5 (group 22): the estimate, the worked time, the pause's reason, and the moves' rules.
  describe('the estimate, the worked time and the pause', () => {
    function property(label: string): HTMLElement | undefined {
      return [...panel().querySelectorAll<HTMLElement>('.property')].find(
        (found) => found.querySelector('dt')?.textContent.trim() === label,
      );
    }

    it('asks for an estimate before a task without one goes into În lucru, and moves with it', async () => {
      await open();

      await (await select('kb-task-column')).clickOptions({ text: 'În lucru' });

      expect(questions.estimate).toHaveBeenCalledWith('KAN-1');
      expect(api['move']).toHaveBeenCalledWith('t1', 'c2', undefined, { estimatedMinutes: 60 });
    });

    it('puts the select back, asking the API nothing, when the estimate is turned down', async () => {
      questions.estimate.mockReturnValue(Promise.resolve(null));
      await open();

      await (await select('kb-task-column')).clickOptions({ text: 'În lucru' });
      await settle();

      expect(api['move']).not.toHaveBeenCalled();
      expect(await (await select('kb-task-column')).getValueText()).toBe('De făcut');
    });

    it('asks why before a pause, and shows the reason under Stare', async () => {
      api['project'].mockReturnValue(of({ ...aProject(), columns: PANEL_COLUMNS }));
      api['move'] = answer({
        columnId: 'c4',
        columnName: 'În pauză',
        pauseReason: 'Aștept răspunsul clientului',
      });
      await open();

      await (await select('kb-task-column')).clickOptions({ text: 'În pauză' });
      await settle();

      expect(questions.pauseReason).toHaveBeenCalledWith('KAN-1');
      expect(api['move']).toHaveBeenCalledWith('t1', 'c4', undefined, {
        pauseReason: 'Aștept clientul',
      });
      expect(property('Motivul pauzei')?.querySelector('dd')?.textContent.trim()).toBe(
        'Aștept răspunsul clientului',
      );
    });

    it('turns off the columns before În lucru for a started task', async () => {
      task = { ...task, columnId: 'c2', columnName: 'În lucru', estimatedMinutes: 60 };
      await open();

      const column = await select('kb-task-column');
      await column.open();
      const options = await column.getOptions();
      const states = await Promise.all(
        options.map(
          async (option) => `${await option.getText()} ${String(await option.isDisabled())}`,
        ),
      );

      expect(states).toEqual(['De făcut true', 'În lucru false', 'Gata false']);
    });

    it('edits the estimate in place: Enter saves it in minutes, with a comma', async () => {
      api['estimate'] = vi.fn((_id: string, estimatedMinutes: number | null) =>
        answer({ estimatedMinutes })(),
      );
      await open();
      const estimate = property('Estimare')!;
      expect(visibleText(estimate.querySelector('dd')!)).toBe('Fără estimare');

      estimate.querySelector<HTMLButtonElement>('.value-button')!.click();
      await settle();
      const field = estimate.querySelector<HTMLInputElement>('.estimate-field')!;
      expect(document.activeElement).toBe(field);
      field.value = '1,5';
      field.dispatchEvent(new Event('input'));
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();

      expect(api['estimate']).toHaveBeenCalledWith('t1', 90);
      expect(visibleText(estimate.querySelector('dd')!)).toBe('1 h 30 min');
    });

    it('refuses an estimate out of limits under its field, and Esc goes back without closing', async () => {
      api['estimate'] = vi.fn();
      await open();
      const estimate = property('Estimare')!;
      estimate.querySelector<HTMLButtonElement>('.value-button')!.click();
      await settle();
      const field = estimate.querySelector<HTMLInputElement>('.estimate-field')!;

      field.value = '1001';
      field.dispatchEvent(new Event('input'));
      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();
      expect(estimate.querySelector('.kb-field-error')?.textContent.trim()).toBe(
        'Între 0,25 și 1000 de ore.',
      );

      field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await settle();
      expect(api['estimate']).not.toHaveBeenCalled();
      expect(estimate.querySelector('.estimate-field')).toBeNull();
      expect(panel()).not.toBeNull();
    });

    it('shows the time worked, counting now in În lucru, against the estimate, danger once over', async () => {
      task = {
        ...task,
        columnId: 'c2',
        columnName: 'În lucru',
        estimatedMinutes: 120,
        workedSeconds: 3 * 3600 + 600,
        isWorking: true,
      };
      await open();
      const worked = property('Timp lucrat')!;

      expect(
        worked.querySelector('.worked-time')!.textContent.replace('play_circle', '').trim(),
      ).toBe('3 h 10 min');
      const counting = worked.querySelector('.working')!;
      expect(counting.textContent.trim()).toBe('play_circle');
      expect(counting.getAttribute('role')).toBe('img');
      expect(getDebugNode(counting)!.injector.get(MatTooltip).message).toBe(
        'Se numără acum, în programul de lucru.',
      );
      const estimate = worked.querySelector('.of-estimate')!;
      expect(estimate.textContent.trim()).toBe('din 2 h estimate');
      expect(estimate.classList).toContain('over');
    });

    it('shows the time worked without the counting icon or the estimate when neither applies', async () => {
      await open();
      const worked = property('Timp lucrat')!;

      expect(visibleText(worked.querySelector('.worked-time')!)).toBe('0 min');
      expect(worked.querySelector('.working')).toBeNull();
      expect(worked.querySelector('.of-estimate')).toBeNull();
    });

    it('reads the task again for its files from Reîncarcă on Atașamente', async () => {
      await open();
      const tabs = await overlay.getHarness(MatTabGroupHarness);
      await tabs.selectTab({ label: 'Atașamente' });
      await settle();

      panel()
        .querySelector<HTMLButtonElement>('kb-task-attachments kb-refresh-button button')!
        .click();
      await settle();

      expect(api['byKey']).toHaveBeenCalledTimes(2);
    });
  });
});

/**
 * Closes a list as a person does, with Esc on its trigger. MatSelectHarness.close clicks the first
 * backdrop of the page, which in a sheet or the panel is the dialog's, and closes that instead.
 */
async function closeList(select: MatSelectHarness): Promise<void> {
  await (await select.host()).sendKeys(TestKey.ESCAPE);
}
