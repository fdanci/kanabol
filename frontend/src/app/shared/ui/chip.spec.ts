import { TestBed } from '@angular/core/testing';
import { Chip, ChipKind } from './chip';

describe('Chip', () => {
  async function chip(kind: ChipKind): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Chip);
    fixture.componentRef.setInput('kind', kind);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  // Not by color alone (docs/UI_UX_SPEC.md §9).
  it('gives each priority and deadline an icon a screen reader skips', async () => {
    for (const [kind, icon] of [
      ['low', 'arrow_downward'],
      ['medium', 'remove'],
      ['high', 'arrow_upward'],
      ['critical', 'priority_high'],
      ['deadline-overdue', 'event_busy'],
    ] as const) {
      const host = await chip(kind);
      const shown = host.querySelector('.material-symbols-outlined')!;
      expect(shown.textContent.trim()).toBe(icon);
      expect(shown.getAttribute('aria-hidden')).toBe('true');
      expect(host.classList).toContain(kind);
    }
  });

  it('has no icon for a role or a plain label', async () => {
    for (const kind of ['owner', 'member', 'archived', 'neutral'] as const) {
      expect((await chip(kind)).querySelector('.material-symbols-outlined')).toBeNull();
    }
  });
});
