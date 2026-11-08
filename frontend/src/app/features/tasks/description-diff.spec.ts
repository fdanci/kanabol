import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DescriptionDiff, descriptionDiff } from './description-diff';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DescriptionDiff],
  template: `<kb-description-diff [before]="before()" [after]="after()" />`,
})
class Host {
  readonly before = signal<string | null>(null);
  readonly after = signal<string | null>(null);
}

/**
 * docs/UI_UX_SPEC.md §7.5 "Diferența descrierii": word by word, on the markdown source, the deletions
 * struck through on danger-bg, the insertions on success-bg, the rest as it is, the source's lines kept.
 */
describe('DescriptionDiff', () => {
  let fixture: ComponentFixture<Host>;

  function shown(before: string | null, after: string | null): HTMLElement {
    fixture = TestBed.createComponent(Host);
    fixture.componentInstance.before.set(before);
    fixture.componentInstance.after.set(after);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function texts(root: HTMLElement, selector: string): string[] {
    return [...root.querySelectorAll(selector)].map((element) => element.textContent);
  }

  it('marks a word taken out as deleted, and the words around it as they are', () => {
    const root = shown('Cu **Azure AD** și SSO', 'Cu **Azure** și SSO');

    expect(texts(root, 'del')).toEqual([' AD']);
    expect(texts(root, 'ins')).toEqual([]);
    expect(root.querySelector('.diff')?.textContent).toBe('Cu **Azure AD** și SSO');
  });

  it('marks a word put in as added', () => {
    const root = shown('Login cu SSO', 'Login rapid cu SSO');

    expect(texts(root, 'ins')).toEqual(['rapid ']);
    expect(texts(root, 'del')).toEqual([]);
    expect(root.querySelector('.diff')?.textContent).toBe('Login rapid cu SSO');
  });

  it('shows a word replaced as the old one deleted, then the new one added', () => {
    const root = shown('Cu **Azure AD**', 'Cu **Entra ID**');

    expect(texts(root, 'del')).toEqual(['Azure', 'AD']);
    expect(texts(root, 'ins')).toEqual(['Entra', 'ID']);
    expect(root.querySelector('.diff')?.textContent).toBe('Cu **AzureEntra ADID**');
  });

  it('keeps the lines of the markdown source, a new line among them', () => {
    const root = shown('Pași:\n\n- login\n- logout', 'Pași:\n\n- login\n- refresh\n- logout');

    expect(root.querySelector('.diff')?.textContent).toBe('Pași:\n\n- login\n- refresh\n- logout');
    expect(texts(root, 'ins').join('')).toBe('refresh\n- ');
    expect(texts(root, 'del')).toEqual([]);
  });

  it('shows a first description as added whole, and one taken away as deleted whole', () => {
    const first = shown(null, 'Prima descriere');
    expect(texts(first, 'ins')).toEqual(['Prima descriere']);
    expect(texts(first, 'del')).toEqual([]);

    const gone = shown('De șters', null);
    expect(texts(gone, 'del')).toEqual(['De șters']);
    expect(texts(gone, 'ins')).toEqual([]);
  });

  it('keeps markdown and HTML in the texts as plain text', () => {
    const root = shown('Vechi', '<img src=x onerror=alert(1)> **nou**');

    expect(root.querySelector('img')).toBeNull();
    expect(root.querySelector('strong')).toBeNull();
    expect(texts(root, 'ins').join('')).toBe('<img src=x onerror=alert(1)> **nou**');
  });
});

describe('descriptionDiff', () => {
  it('gives the parts in order, each kept, deleted or added', () => {
    expect(descriptionDiff('a b c', 'a x c')).toEqual([
      { text: 'a ', kind: 'same' },
      { text: 'b', kind: 'removed' },
      { text: 'x', kind: 'added' },
      { text: ' c', kind: 'same' },
    ]);
  });

  it('deletes the old text whole and adds the new one whole when comparing them would take too long', () => {
    expect(descriptionDiff('unu doi', 'trei patru', { maxEditLength: 0 })).toEqual([
      { text: 'unu doi', kind: 'removed' },
      { text: 'trei patru', kind: 'added' },
    ]);
  });

  it('gives nothing for two empty texts', () => {
    expect(descriptionDiff(null, '')).toEqual([]);
  });
});
