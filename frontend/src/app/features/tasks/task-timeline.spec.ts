import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { Page } from '../../shared/models/page';
import { TaskHistoryEntryDto } from './models';
import { TaskTimeline } from './task-timeline';
import { anEntry, refused } from './task-testing';
import { TasksApi } from './tasks-api';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TaskTimeline],
  template: `<kb-task-timeline [taskId]="taskId()" [revision]="revision()" />`,
})
class Host {
  readonly taskId = signal('t1');
  readonly revision = signal(0);
}

function pageOf(
  items: TaskHistoryEntryDto[],
  totalCount = items.length,
  page = 1,
): Page<TaskHistoryEntryDto> {
  return { items, page, pageSize: 50, totalCount };
}

/**
 * docs/UI_UX_SPEC.md §7.5: a vertical timeline from the newest change, 50 at a time with "Încarcă mai
 * multe" at the end while there are more, read when the tab opens and again after each change the panel
 * saves; no SignalR. A description's row offers "Vezi diferența", which reads the entry with its texts
 * (docs/SPEC.md §4.4) and opens the diff under the row.
 */
describe('TaskTimeline', () => {
  let api: { history: ReturnType<typeof vi.fn>; historyEntry: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Host>;

  beforeEach(() => {
    api = {
      history: vi.fn(() =>
        of(
          pageOf([
            anEntry({ id: 'h2', field: 'Priority', oldValue: 'Medium', newValue: 'High' }),
            anEntry({
              id: 'h1',
              field: 'Title',
              oldValue: 'Login SSO',
              newValue: 'Login cu SSO',
              changedBy: { userId: 'u2', displayName: 'Ana Pop' },
              changedAt: '2026-11-07T08:00:00Z',
            }),
          ]),
        ),
      ),
      historyEntry: vi.fn(() =>
        of(
          anEntry({
            id: 'h3',
            field: 'Description',
            oldValue: 'Cu **Azure AD**',
            newValue: 'Cu **Entra ID**\n- un rând nou',
          }),
        ),
      ),
    };
    TestBed.configureTestingModule({ providers: [{ provide: TasksApi, useValue: api }] });
  });

  async function shown(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(Host);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  function entries(): HTMLElement[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.entry')];
  }

  function text(element: Element): string {
    return element.textContent.replace(/\s+/g, ' ').trim();
  }

  function button(label: string, root: Element = fixture.nativeElement as HTMLElement) {
    return [...root.querySelectorAll('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  /** The history with a change of the description on top, as the list gives it: without its texts. */
  async function shownWithADescription(): Promise<HTMLElement> {
    api.history.mockReturnValueOnce(
      of(
        pageOf([
          anEntry({ id: 'h3', field: 'Description' }),
          anEntry({ id: 'h2', field: 'Priority', oldValue: 'Medium', newValue: 'High' }),
        ]),
      ),
    );
    return shown();
  }

  function more(): HTMLButtonElement | undefined {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent.trim() === 'Încarcă mai multe',
    );
  }

  it('shows each change from the newest, as who changed what, and when', async () => {
    await shown();

    expect(api.history).toHaveBeenCalledWith('t1', 1);
    const [newest, older] = entries();
    expect(newest.querySelector('.dot')).not.toBeNull();
    expect(newest.querySelector('strong')?.textContent.trim()).toBe('Florin Danci');
    expect(newest.querySelector('em')?.textContent.trim()).toBe('Prioritate');
    expect(text(newest.querySelector('.line')!)).toMatch(
      /^Florin Danci a schimbat Prioritate: Medium → High · /,
    );
    expect(newest.querySelector('time')?.getAttribute('datetime')).toBe('2026-11-08T10:00:00Z');
    expect(text(older.querySelector('.line')!)).toMatch(
      /^Ana Pop a schimbat Titlu: Login SSO → Login cu SSO · /,
    );
  });

  it('writes the description, an archiving and a restoring as sentences of their own', async () => {
    api.history.mockReturnValueOnce(
      of(
        pageOf([
          anEntry({ id: 'h3', field: 'ArchivedAt', oldValue: '2026-11-08T09:00:00Z' }),
          anEntry({ id: 'h2', field: 'ArchivedAt', newValue: '2026-11-08T09:00:00Z' }),
          anEntry({ id: 'h1', field: 'Description', oldValue: 'Veche', newValue: 'Nouă' }),
        ]),
      ),
    );

    await shown();

    expect(entries().map((entry) => text(entry.querySelector('.what')!))).toEqual([
      'l-a restaurat',
      'a arhivat task-ul',
      'a schimbat descrierea',
    ]);
    expect(text(fixture.nativeElement as HTMLElement)).not.toContain('Veche');
  });

  it('loads 50 more with "Încarcă mai multe", only while there are more', async () => {
    const first = Array.from({ length: 50 }, (_, index) =>
      anEntry({ id: `h${String(51 - index)}`, newValue: `Titlul ${String(51 - index)}` }),
    );
    api.history
      .mockReturnValueOnce(of(pageOf(first, 51)))
      .mockReturnValueOnce(of(pageOf([anEntry({ id: 'h1', newValue: 'Titlul 1' })], 51, 2)));
    await shown();
    expect(entries()).toHaveLength(50);

    more()!.click();
    await settle();

    expect(api.history).toHaveBeenLastCalledWith('t1', 2);
    expect(entries()).toHaveLength(51);
    expect(text(entries()[50])).toContain('Titlul 1');
    expect(more()).toBeUndefined();
  });

  it('keeps each change once when the next page repeats one, after a change came meanwhile', async () => {
    const first = Array.from({ length: 50 }, (_, index) =>
      anEntry({ id: `h${String(52 - index)}` }),
    );
    api.history
      .mockReturnValueOnce(of(pageOf(first, 51)))
      .mockReturnValueOnce(
        of(pageOf([anEntry({ id: 'h3' }), anEntry({ id: 'h2' }), anEntry({ id: 'h1' })], 53, 2)),
      );
    await shown();

    more()!.click();
    await settle();

    expect(entries()).toHaveLength(52);
  });

  it('reads the history again, from the newest, after the panel saves a change', async () => {
    await shown();
    api.history.mockReturnValueOnce(
      of(pageOf([anEntry({ id: 'h3', field: 'DueDate', newValue: '2026-11-12T12:00:00Z' })], 3)),
    );

    fixture.componentInstance.revision.set(1);
    await settle();

    expect(api.history).toHaveBeenCalledTimes(2);
    expect(api.history).toHaveBeenLastCalledWith('t1', 1);
    expect(text(entries()[0].querySelector('.line')!)).toContain(
      'a schimbat Deadline: fără deadline → 12.11.2026',
    );
  });

  it('keeps the changes it shows while it reads them again', async () => {
    await shown();
    const answer = new Subject<Page<TaskHistoryEntryDto>>();
    api.history.mockReturnValueOnce(answer);

    fixture.componentInstance.revision.set(1);
    await settle();

    expect(entries()).toHaveLength(2);
    answer.next(pageOf([anEntry({ id: 'h3' })]));
    answer.complete();
    await settle();
    expect(entries()).toHaveLength(1);
  });

  it('reads the history of another task when the panel shows another', async () => {
    await shown();

    fixture.componentInstance.taskId.set('t2');
    await settle();

    expect(api.history).toHaveBeenLastCalledWith('t2', 1);
  });

  it('offers the difference only on a change of the description', async () => {
    await shownWithADescription();

    const [described, prioritized] = entries();
    const toggle = button('Vezi diferența', described);
    expect(toggle?.getAttribute('aria-expanded')).toBe('false');
    expect(button('Vezi diferența', prioritized)).toBeUndefined();
    expect(described.querySelector('.diff-block')).toBeNull();
    expect(api.historyEntry).not.toHaveBeenCalled();
  });

  it('opens the difference under its row with "Vezi diferența", reading the entry with its texts', async () => {
    await shownWithADescription();
    const described = entries()[0];

    button('Vezi diferența', described)!.click();
    await settle();

    expect(api.historyEntry).toHaveBeenCalledExactlyOnceWith('t1', 'h3');
    const block = described.querySelector('.diff-block')!;
    expect(block.getAttribute('id')).toBe(
      button('Ascunde', described)!.getAttribute('aria-controls'),
    );
    expect(button('Ascunde', described)!.getAttribute('aria-expanded')).toBe('true');
    expect([...block.querySelectorAll('del')].map((part) => part.textContent)).toEqual([
      'Azure',
      'AD',
    ]);
    expect(block.querySelector('ins')?.textContent).toBe('Entra');
    expect(block.querySelector('kb-description-diff')?.textContent).toBe(
      'Cu **AzureEntra ADID**\n- un rând nou',
    );
  });

  it('hides the difference with "Ascunde", and opens it again without reading it again', async () => {
    await shownWithADescription();
    const described = entries()[0];
    button('Vezi diferența', described)!.click();
    await settle();

    button('Ascunde', described)!.click();
    await settle();
    expect(described.querySelector('.diff-block')).toBeNull();
    expect(button('Vezi diferența', described)).toBeDefined();

    button('Vezi diferența', described)!.click();
    await settle();
    expect(described.querySelector('.diff-block del')).not.toBeNull();
    expect(api.historyEntry).toHaveBeenCalledTimes(1);
  });

  it('shows a skeleton of two rows while it reads the entry', async () => {
    const answer = new Subject<TaskHistoryEntryDto>();
    api.historyEntry.mockReturnValueOnce(answer);
    await shownWithADescription();
    const described = entries()[0];

    button('Vezi diferența', described)!.click();
    await settle();

    const block = described.querySelector('.diff-block')!;
    expect(block.getAttribute('aria-busy')).toBe('true');
    expect(block.querySelectorAll('kb-skeleton')).toHaveLength(2);
    answer.next(anEntry({ id: 'h3', field: 'Description', oldValue: 'a', newValue: 'b' }));
    answer.complete();
    await settle();
    expect(block.querySelectorAll('kb-skeleton')).toHaveLength(0);
    expect(block.querySelector('ins')?.textContent).toBe('b');
  });

  it('says it could not read the difference, and reads it again on Reîncearcă', async () => {
    api.historyEntry.mockReturnValueOnce(refused(500, 'Unexpected'));
    await shownWithADescription();
    const described = entries()[0];

    button('Vezi diferența', described)!.click();
    await settle();
    const block = described.querySelector('.diff-block')!;
    expect(block.querySelector('.empty')?.textContent.trim()).toBe(
      'Nu am putut încărca diferența.',
    );

    button('Reîncearcă', block)!.click();
    await settle();

    expect(api.historyEntry).toHaveBeenCalledTimes(2);
    expect(described.querySelector('.diff-block del')).not.toBeNull();
    expect(described.querySelector('.diff-block .empty')).toBeNull();
  });

  it('keeps an open difference open when it reads the history again after a save', async () => {
    await shownWithADescription();
    button('Vezi diferența', entries()[0])!.click();
    await settle();
    api.history.mockReturnValueOnce(
      of(
        pageOf([
          anEntry({ id: 'h4', field: 'Title', oldValue: 'Login SSO', newValue: 'Login' }),
          anEntry({ id: 'h3', field: 'Description' }),
        ]),
      ),
    );

    fixture.componentInstance.revision.set(1);
    await settle();

    expect(entries()[1].querySelector('.diff-block del')).not.toBeNull();
    expect(api.historyEntry).toHaveBeenCalledTimes(1);
  });

  it('starts with every difference closed for another task', async () => {
    await shownWithADescription();
    button('Vezi diferența', entries()[0])!.click();
    await settle();
    api.history.mockReturnValueOnce(of(pageOf([anEntry({ id: 'h3', field: 'Description' })])));

    fixture.componentInstance.taskId.set('t2');
    await settle();
    button('Vezi diferența', entries()[0])!.click();
    await settle();

    expect(api.historyEntry).toHaveBeenLastCalledWith('t2', 'h3');
    expect(api.historyEntry).toHaveBeenCalledTimes(2);
  });

  it('says when nothing has changed yet', async () => {
    api.history.mockReturnValueOnce(of(pageOf([])));

    const root = await shown();

    expect(root.querySelector('.empty')?.textContent.trim()).toBe('Nicio schimbare încă.');
    expect(more()).toBeUndefined();
  });

  it('says it could not read the history, and reads it again on Reîncearcă', async () => {
    api.history.mockReturnValueOnce(refused(500, 'Unexpected'));
    const root = await shown();
    expect(root.querySelector('.empty')?.textContent.trim()).toBe('Nu am putut încărca istoricul.');

    [...root.querySelectorAll('button')]
      .find((button) => button.textContent.trim() === 'Reîncearcă')!
      .click();
    await settle();

    expect(entries()).toHaveLength(2);
  });
});
