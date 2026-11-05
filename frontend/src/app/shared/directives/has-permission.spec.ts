import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthStore } from '../../core/auth/auth-store';
import { HasPermission } from './has-permission';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HasPermission],
  template: `<a *kbHasPermission="'users.manage'" href="/users" i18n>Utilizatori</a>`,
})
class Host {}

describe('HasPermission', () => {
  const held = signal<string[]>([]);

  beforeEach(() => {
    held.set([]);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { has: (name: string) => held().includes(name) } },
      ],
    });
  });

  async function rendered(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows what needs a permission the user has', async () => {
    held.set(['users.manage']);

    expect((await rendered()).querySelector('a')?.textContent).toBe('Utilizatori');
  });

  // Nothing disabled with "you may not": it isn't there (docs/UI_UX_SPEC.md §6.8).
  it('leaves it out without the permission', async () => {
    held.set(['reports.view']);

    expect((await rendered()).querySelector('a')).toBeNull();
  });

  it('follows the permissions as they change', async () => {
    held.set(['users.manage']);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();

    held.set([]);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('a')).toBeNull();

    held.set(['users.manage']);
    await fixture.whenStable();
    expect(element.querySelectorAll('a')).toHaveLength(1);
  });
});
