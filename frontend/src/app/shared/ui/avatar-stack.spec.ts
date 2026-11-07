import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { MatTooltipHarness } from '@angular/material/tooltip/testing';
import { AvatarStack, StackedPerson } from './avatar-stack';

const ANA = { userId: 'u1', displayName: 'Ana Pop' };
const DAN = { userId: 'u2', displayName: 'Dan Ionescu' };
const EVA = { userId: 'u3', displayName: 'Eva Ene' };
const IOANA = { userId: 'u4', displayName: 'Ioana Radu' };
const MIHAI = { userId: 'u5', displayName: 'Mihai Stan' };

function stackOf(people: StackedPerson[]) {
  const fixture = TestBed.createComponent(AvatarStack);
  fixture.componentRef.setInput('people', people);
  return fixture;
}

// docs/UI_UX_SPEC.md §7.3 and §7.4: overlapping avatars of 24px, at most 3 and "+N", every name in
// the tooltip and in the aria-label.
describe('AvatarStack', () => {
  it('shows everyone up to three, without +N', async () => {
    const fixture = stackOf([ANA, DAN, EVA]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    const initials = [...host.querySelectorAll('kb-avatar')].map((avatar) =>
      avatar.textContent.trim(),
    );
    expect(initials).toEqual(['AP', 'DI', 'EE']);
    expect(host.querySelector('.more')).toBeNull();
  });

  it('shows the first three of more, and how many are left', async () => {
    const fixture = stackOf([ANA, DAN, EVA, IOANA, MIHAI]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('kb-avatar').length).toBe(3);
    expect(host.querySelector('.more')?.textContent.trim()).toBe('+2');
  });

  it('names everyone once for a screen reader, the avatars and +N staying quiet', async () => {
    const fixture = stackOf([ANA, DAN, EVA, IOANA]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('Ana Pop, Dan Ionescu, Eva Ene, Ioana Radu');
    for (const avatar of host.querySelectorAll('kb-avatar')) {
      expect(avatar.getAttribute('aria-hidden')).toBe('true');
    }
    expect(host.querySelector('.more')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('puts every name in the tooltip', async () => {
    const fixture = stackOf([ANA, DAN, EVA, IOANA]);
    const tooltip = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatTooltipHarness);

    await tooltip.show();

    expect(await tooltip.getTooltipText()).toBe('Ana Pop, Dan Ionescu, Eva Ene, Ioana Radu');
  });

  it('shows nothing and says nothing for nobody', async () => {
    const fixture = stackOf([]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('kb-avatar')).toBeNull();
    expect(host.getAttribute('role')).toBeNull();
    expect(host.getAttribute('aria-label')).toBeNull();
  });
});
