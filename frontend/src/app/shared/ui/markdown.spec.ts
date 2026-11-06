import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Markdown } from './markdown';

@Component({
  imports: [Markdown],
  template: `<kb-markdown [text]="text()" />`,
})
class Host {
  readonly text = signal('');
}

describe('Markdown', () => {
  function render(text: string): HTMLElement {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.text.set(text);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('kb-markdown')!;
  }

  it('renders headings, paragraphs, lists, code, quotes and rules as their elements', () => {
    const page = render(
      '# Titlu\n\nUn **pas** și *altul*, cu `cod`\na doua linie\n\n- unu\n- doi\n\n1. primul\n\n```\nx = 1\n```\n\n> citat\n\n---',
    );

    expect(page.querySelector('h1')?.textContent).toBe('Titlu');
    expect(page.querySelector('p strong')?.textContent).toBe('pas');
    expect(page.querySelector('p em')?.textContent).toBe('altul');
    expect(page.querySelector('p code')?.textContent).toBe('cod');
    expect(page.querySelector('p br')).not.toBeNull();
    expect([...page.querySelectorAll('ul li')].map((item) => item.textContent)).toEqual([
      'unu',
      'doi',
    ]);
    expect(page.querySelector('ol li')?.textContent).toBe('primul');
    expect(page.querySelector('pre code')?.textContent).toBe('x = 1');
    expect(page.querySelector('blockquote')?.textContent).toBe('citat');
    expect(page.querySelector('hr')).not.toBeNull();
  });

  // docs/SPEC.md §16: no raw HTML, no script, no on* attributes, links that can't reach back.
  it('never puts raw HTML into the page', () => {
    const page = render(
      '<script>alert(1)</script><img src=x onerror="alert(2)"><b onclick="alert(3)">gras</b><iframe src="https://x"></iframe>\n\n<a href="javascript:alert(4)">link</a>',
    );

    expect(page.querySelectorAll('script, img, iframe, b, a').length).toBe(0);
    const everything = [page, ...page.querySelectorAll('*')];
    expect(
      everything
        .flatMap((element) => [...element.attributes].map((attribute) => attribute.name))
        .filter((name) => name.startsWith('on')),
    ).toEqual([]);
    expect(page.textContent).toContain('gras');
  });

  it('opens links in a new tab, with nothing to reach back to the page', () => {
    const link = render('[Kanabol](https://kanabol.local/tasks/KAN-1)').querySelector('a')!;

    expect(link.getAttribute('href')).toBe('https://kanabol.local/tasks/KAN-1');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.textContent).toBe('Kanabol');
  });

  it('turns a javascript: or data: link into its text', () => {
    const page = render('[rău](javascript:alert(1)) și [date](data:text/html,x)');

    expect(page.querySelector('a')).toBeNull();
    expect(page.textContent).toContain('[rău](javascript:alert(1))');
  });

  it('shows markup in code as text', () => {
    const code = render('```\n<script>alert(1)</script>\n```').querySelector('pre code')!;

    expect(code.textContent).toBe('<script>alert(1)</script>');
    expect(code.querySelector('script')).toBeNull();
  });

  it('renders nothing for blank text', () => {
    expect(render('  \n ').children.length).toBe(0);
  });
});
