import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';
import { Skeleton } from './skeleton';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState],
  template: `
    <kb-empty-state icon="task_alt" i18n-title title="Niciun task încă">
      <p i18n>Task-urile proiectului apar aici.</p>
      <button kbEmptyAction type="button" i18n>Creează primul task</button>
    </kb-empty-state>
    <kb-empty-state icon="folder" i18n-title title="Niciun proiect" />
  `,
})
class Host {}

describe('EmptyState', () => {
  it('shows the icon, the title, the sentence and the action', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const [full] = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('kb-empty-state'),
    );

    expect(full.querySelector('.material-symbols-outlined')?.textContent).toBe('task_alt');
    expect(full.querySelector('.circle')?.getAttribute('aria-hidden')).toBe('true');
    expect(full.querySelector('h3')?.textContent).toBe('Niciun task încă');
    expect(full.querySelector('.text')?.textContent).toContain('Task-urile proiectului apar aici.');
    expect(full.querySelector('.action button')?.textContent).toBe('Creează primul task');
  });

  // Without the permission there's no action, and no empty space for one.
  it('leaves the action out when there is none', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const bare = (fixture.nativeElement as HTMLElement).querySelectorAll('kb-empty-state')[1];

    expect(bare.querySelector('.action')?.children).toHaveLength(0);
  });
});

describe('Skeleton', () => {
  it('takes the size of what it stands for, and hides from screen readers', async () => {
    const fixture = TestBed.createComponent(Skeleton);
    fixture.componentRef.setInput('width', '120px');
    fixture.componentRef.setInput('height', '32px');
    fixture.componentRef.setInput('circle', true);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.style.width).toBe('120px');
    expect(host.style.height).toBe('32px');
    expect(host.classList).toContain('circle');
    expect(host.getAttribute('aria-hidden')).toBe('true');
  });
});
