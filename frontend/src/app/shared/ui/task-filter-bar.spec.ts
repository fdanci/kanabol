import { Component, signal } from '@angular/core';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSelectHarness } from '@angular/material/select/testing';
import { NO_TASK_FILTERS, TaskFilters } from '../models/task-filters';
import { SEARCH_DELAY_MS, TaskFilterBar } from './task-filter-bar';

@Component({
  imports: [TaskFilterBar],
  template: `<kb-task-filter-bar
    [filters]="filters()"
    [people]="people"
    [canSeeArchived]="canSeeArchived()"
    [withAssignee]="withAssignee()"
    [finished]="finished()"
    (filtersChange)="changed.push($event); filters.set($event)"
    (finishedChange)="finishedChanges.push($event); finished.set($event)"
  />`,
})
class Host {
  readonly filters = signal<TaskFilters>(NO_TASK_FILTERS);
  readonly canSeeArchived = signal(false);
  readonly withAssignee = signal(true);
  readonly finished = signal<boolean | null>(null);
  readonly finishedChanges: boolean[] = [];
  readonly people = [
    { userId: 'u1', displayName: 'Ana Pop' },
    { userId: 'u2', displayName: 'Dan Ionescu' },
  ];
  readonly changed: TaskFilters[] = [];
}

describe('TaskFilterBar', () => {
  let fixture: ComponentFixture<Host>;
  let loader: HarnessLoader;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }],
    });
    fixture = TestBed.createComponent(Host);
    loader = TestbedHarnessEnvironment.loader(fixture);
    fixture.detectChanges();
  });

  function chip(label: string): HTMLButtonElement {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        'button.kb-filter-chip',
      ),
    ].find((button) => button.textContent.trim() === label)!;
  }

  function last(): TaskFilters {
    return fixture.componentInstance.changed.at(-1)!;
  }

  it('turns Ale mele and Întârziate on and off, as pressed toggles', () => {
    chip('Ale mele').click();
    fixture.detectChanges();
    expect(last()).toEqual({ ...NO_TASK_FILTERS, assignee: 'me' });
    expect(chip('Ale mele').getAttribute('aria-pressed')).toBe('true');

    chip('Întârziate').click();
    fixture.detectChanges();
    chip('Ale mele').click();
    fixture.detectChanges();
    expect(last()).toEqual({ ...NO_TASK_FILTERS, overdue: true });
  });

  it('picks one assignee, or anyone again', async () => {
    const assignee = await loader.getHarness(MatSelectHarness.with({ selector: '.assignee' }));

    await assignee.clickOptions({ text: /Dan Ionescu/ });
    expect(last().assignee).toBe('u2');

    await assignee.clickOptions({ text: 'Oricine' });
    expect(last().assignee).toBeNull();
  });

  it('picks several priorities at once', async () => {
    const priority = await loader.getHarness(MatSelectHarness.with({ selector: '.priority' }));

    await priority.clickOptions({ text: 'High' });
    await priority.clickOptions({ text: 'Critical' });

    expect(last().priorities).toEqual(['High', 'Critical']);
  });

  it('searches once the typing stops', () => {
    vi.useFakeTimers();
    try {
      const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
        'input[type=search]',
      )!;
      input.value = 'log';
      input.dispatchEvent(new Event('input'));
      input.value = 'login';
      input.dispatchEvent(new Event('input'));
      expect(fixture.componentInstance.changed).toEqual([]);

      vi.advanceTimersByTime(SEARCH_DELAY_MS);

      expect(fixture.componentInstance.changed).toEqual([{ ...NO_TASK_FILTERS, search: 'login' }]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('offers archived tasks only to whoever may see them', () => {
    expect(chip('Și cele arhivate')).toBeUndefined();

    fixture.componentInstance.canSeeArchived.set(true);
    fixture.detectChanges();
    chip('Și cele arhivate').click();

    expect(last().archived).toBe(true);
  });

  // docs/UI_UX_SPEC.md §7.2: "Task-urile mele" are the user's own, without an assignee to pick.
  it('leaves out Ale mele and the assignee select when the tasks are all the user’s', () => {
    fixture.componentInstance.withAssignee.set(false);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(chip('Ale mele')).toBeUndefined();
    expect(host.querySelector('mat-select.assignee')).toBeNull();
    expect(host.querySelector('mat-select.priority')).not.toBeNull();
    expect(chip('Întârziate')).toBeDefined();
  });

  it('offers "Și cele terminate" only when given, and says when it is pressed', () => {
    expect(chip('Și cele terminate')).toBeUndefined();

    fixture.componentInstance.finished.set(false);
    fixture.detectChanges();
    expect(chip('Și cele terminate').getAttribute('aria-pressed')).toBe('false');
    chip('Și cele terminate').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.finishedChanges).toEqual([true]);
    expect(chip('Și cele terminate').getAttribute('aria-pressed')).toBe('true');
    expect(fixture.componentInstance.changed).toEqual([]);
  });

  it('clears the filters, keeping the archived ones as chosen', () => {
    fixture.componentInstance.filters.set({
      ...NO_TASK_FILTERS,
      overdue: true,
      search: 'x',
      archived: true,
    });
    fixture.componentInstance.canSeeArchived.set(true);
    fixture.detectChanges();
    const clear = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent.trim() === 'Șterge filtrele',
    )!;

    clear.click();

    expect(last()).toEqual({ ...NO_TASK_FILTERS, archived: true });
  });
});
