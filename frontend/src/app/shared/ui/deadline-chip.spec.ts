import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DeadlineChip, deadlineKind, formatDay } from './deadline-chip';

const NOW = new Date('2026-11-06T12:00:00Z').getTime();
const DAY = 24 * 60 * 60 * 1000;

describe('deadlineKind', () => {
  // docs/UI_UX_SPEC.md §6.5: green over 3 days, warning within 3, danger once past.
  it('is ok with more than three days left, soon within three, overdue once past', () => {
    expect(deadlineKind(new Date(NOW + 3 * DAY + 1000).toISOString(), false, NOW)).toBe(
      'deadline-ok',
    );
    expect(deadlineKind(new Date(NOW + 3 * DAY).toISOString(), false, NOW)).toBe('deadline-soon');
    expect(deadlineKind(new Date(NOW + 1000).toISOString(), false, NOW)).toBe('deadline-soon');
    expect(deadlineKind(new Date(NOW - 1000).toISOString(), false, NOW)).toBe('deadline-overdue');
  });

  it('is neutral for a finished task, whenever its deadline was', () => {
    expect(deadlineKind(new Date(NOW - DAY).toISOString(), true, NOW)).toBe('neutral');
  });
});

describe('formatDay', () => {
  it('writes the day as dd.MM.yyyy, where the person is', () => {
    expect(formatDay(new Date(2026, 10, 4, 23, 59).toISOString())).toBe('04.11.2026');
  });
});

@Component({
  imports: [DeadlineChip],
  template: `<kb-deadline-chip [dueDate]="due()" [done]="done()" />`,
})
class Host {
  readonly due = signal(new Date(Date.now() - DAY).toISOString());
  readonly done = signal(false);
}

describe('DeadlineChip', () => {
  it('shows the day in the chip of its state, with an icon', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const chip = (fixture.nativeElement as HTMLElement).querySelector('kb-chip')!;

    expect(chip.classList).toContain('deadline-overdue');
    expect(chip.textContent).toContain(formatDay(fixture.componentInstance.due()));
    expect(chip.querySelector('.material-symbols-outlined')?.textContent).toBe('event_busy');
  });
});
