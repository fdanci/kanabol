import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { Banner, BannerVariant } from './banner';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Banner],
  template: `<kb-banner
    [variant]="variant()"
    [dismissible]="dismissible()"
    (dismissed)="closed = true"
    ><span i18n>Acest proiect e arhivat.</span></kb-banner
  >`,
})
class Host {
  readonly variant = signal<BannerVariant>('info');
  readonly dismissible = signal(false);
  closed = false;
}

describe('Banner', () => {
  async function rendered(variant: BannerVariant, dismissible = false) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.variant.set(variant);
    fixture.componentInstance.dismissible.set(dismissible);
    await fixture.whenStable();
    const banner = (fixture.nativeElement as HTMLElement).querySelector('kb-banner')!;
    return { fixture, banner };
  }

  it('shows its text with the icon of its kind, which a screen reader skips', async () => {
    const { banner } = await rendered('info');

    expect(banner.textContent).toContain('Acest proiect e arhivat.');
    const icon = banner.querySelector('.icon')!;
    expect(icon.textContent.trim()).toBe('info');
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(banner.getAttribute('role')).toBe('status');
  });

  it('has a kind for each state, and the serious ones are alerts', async () => {
    for (const [variant, icon, role] of [
      ['success', 'check_circle', 'status'],
      ['warning', 'warning', 'alert'],
      ['danger', 'error', 'alert'],
    ] as const) {
      const { banner } = await rendered(variant);
      expect(banner.classList).toContain(variant);
      expect(banner.querySelector('.icon')!.textContent.trim()).toBe(icon);
      expect(banner.getAttribute('role')).toBe(role);
    }
  });

  it('has no close button unless asked', async () => {
    const { banner } = await rendered('info');

    expect(banner.querySelector('button')).toBeNull();
  });

  it('can be closed', async () => {
    const { fixture } = await rendered('info', true);
    const close = await TestbedHarnessEnvironment.loader(fixture).getHarness(
      MatButtonHarness.with({ selector: '[aria-label="Închide"]' }),
    );

    await close.click();

    expect(fixture.componentInstance.closed).toBe(true);
  });
});
