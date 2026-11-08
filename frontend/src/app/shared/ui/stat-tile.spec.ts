import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StatTile } from './stat-tile';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StatTile],
  template: `
    <kb-stat-tile
      class="linked"
      label="Întârziate"
      [value]="overdue()"
      caption="după deadline"
      link="/my-tasks"
      [queryParams]="{ overdue: 1 }"
    />
    <kb-stat-tile
      class="plain"
      label="Finalizate săptămâna asta"
      [value]="1234"
      caption="de luni încoace"
    />
  `,
})
class Host {
  readonly overdue = signal<number | null>(3);
}

/** docs/UI_UX_SPEC.md §7.2: a card with a caption, a 28px number and a caption, a link when it leads somewhere. */
describe('StatTile', () => {
  async function render(): Promise<{
    host: Host;
    element: HTMLElement;
    detect: () => Promise<void>;
  }> {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return {
      host: fixture.componentInstance,
      element: fixture.nativeElement as HTMLElement,
      detect: () => fixture.whenStable(),
    };
  }

  it('shows the label, the number and the caption under it', async () => {
    const { element } = await render();
    const tile = element.querySelector('.plain')!;

    expect(tile.querySelector('.label')?.textContent.trim()).toBe('Finalizate săptămâna asta');
    expect(tile.querySelector('.value')?.textContent.trim()).toBe('1.234');
    expect(tile.querySelector('.caption')?.textContent.trim()).toBe('de luni încoace');
  });

  it('is a link with its query when it leads somewhere, and a plain card when not', async () => {
    const { element } = await render();

    const link = element.querySelector<HTMLAnchorElement>('.linked a.tile');
    expect(link?.getAttribute('href')).toBe('/my-tasks?overdue=1');
    expect(link?.textContent).toContain('Întârziate');
    expect(element.querySelector('.plain a')).toBeNull();
    expect(element.querySelector('.plain div.tile')).not.toBeNull();
  });

  // The tile keeps its shape while the number is on its way (docs/UI_UX_SPEC.md §6.8).
  it('shows a skeleton in place of the number until it comes', async () => {
    const { host, element, detect } = await render();
    host.overdue.set(null);
    await detect();
    const tile = element.querySelector('.linked')!;

    expect(tile.querySelector('.value kb-skeleton')).not.toBeNull();
    expect(tile.querySelector('.label')?.textContent.trim()).toBe('Întârziate');

    host.overdue.set(0);
    await detect();
    expect(tile.querySelector('.value')?.textContent.trim()).toBe('0');
  });
});
