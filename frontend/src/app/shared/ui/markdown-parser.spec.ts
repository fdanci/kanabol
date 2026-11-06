import { Block, Inline, parseMarkdown } from './markdown-parser';

function text(value: string): Inline {
  return { type: 'text', text: value };
}

function paragraph(...inlines: Inline[]): Block {
  return { type: 'paragraph', inlines };
}

describe('parseMarkdown', () => {
  it('reads nothing from blank text', () => {
    expect(parseMarkdown('')).toEqual([]);
    expect(parseMarkdown('  \n\n \t')).toEqual([]);
  });

  it('splits paragraphs on blank lines and keeps a single line break inside one', () => {
    expect(parseMarkdown('Unu\ndoi\n\nTrei')).toEqual([
      paragraph(text('Unu'), { type: 'break' }, text('doi')),
      paragraph(text('Trei')),
    ]);
  });

  it('reads headings of one to three marks, and more as text', () => {
    expect(parseMarkdown('# Mare\n## Mediu\n### Mic\n#### Tot mic')).toEqual([
      { type: 'heading', level: 1, inlines: [text('Mare')] },
      { type: 'heading', level: 2, inlines: [text('Mediu')] },
      { type: 'heading', level: 3, inlines: [text('Mic')] },
      paragraph(text('#### Tot mic')),
    ]);
  });

  it('reads bold, italic and inline code', () => {
    expect(parseMarkdown('Un **pas** _mic_ și *altul* cu `cod`')).toEqual([
      paragraph(
        text('Un '),
        { type: 'strong', children: [text('pas')] },
        text(' '),
        { type: 'em', children: [text('mic')] },
        text(' și '),
        { type: 'em', children: [text('altul')] },
        text(' cu '),
        { type: 'code', text: 'cod' },
      ),
    ]);
  });

  it('leaves underscores inside words alone', () => {
    expect(parseMarkdown('task_items și due_date')).toEqual([
      paragraph(text('task_items și due_date')),
    ]);
  });

  it('reads lists, ordered or not, and keeps their items apart', () => {
    expect(parseMarkdown('- unu\n* doi\n\n1. primul\n2) al doilea')).toEqual([
      { type: 'list', ordered: false, items: [[text('unu')], [text('doi')]] },
      { type: 'list', ordered: true, items: [[text('primul')], [text('al doilea')]] },
    ]);
  });

  it('keeps a fenced code block as it was written, markup and all', () => {
    expect(parseMarkdown('```ts\nconst a = "<b>**x**</b>";\n  return a;\n```')).toEqual([
      { type: 'code', text: 'const a = "<b>**x**</b>";\n  return a;' },
    ]);
  });

  it('reads a quote and a rule', () => {
    expect(parseMarkdown('> Spus de\n> cineva\n\n---')).toEqual([
      { type: 'quote', inlines: [text('Spus de'), { type: 'break' }, text('cineva')] },
      { type: 'rule' },
    ]);
  });

  it('links only to http, https and mailto, and leaves anything else as text', () => {
    expect(parseMarkdown('[site](https://kanabol.local/a?b=1) [mail](mailto:ana@x.ro)')).toEqual([
      paragraph(
        { type: 'link', href: 'https://kanabol.local/a?b=1', children: [text('site')] },
        text(' '),
        { type: 'link', href: 'mailto:ana@x.ro', children: [text('mail')] },
      ),
    ]);
    expect(parseMarkdown('[rău](javascript:alert(1)) [date](data:text/html,x)')).toEqual([
      paragraph(text('[rău](javascript:alert(1)) [date](data:text/html,x)')),
    ]);
  });

  it('links an address written as is, without the punctuation after it', () => {
    expect(parseMarkdown('Vezi https://kanabol.local/tasks/KAN-1.')).toEqual([
      paragraph(
        text('Vezi '),
        {
          type: 'link',
          href: 'https://kanabol.local/tasks/KAN-1',
          children: [text('https://kanabol.local/tasks/KAN-1')],
        },
        text('.'),
      ),
    ]);
  });

  // docs/SPEC.md §16: raw HTML is removed, so it can't reach the page.
  it('drops raw HTML tags and comments, keeping only the text between them', () => {
    expect(
      parseMarkdown(
        '<script>alert(1)</script> <img src=x onerror=alert(1)> <b onclick="x">gras</b><!-- ascuns -->',
      ),
    ).toEqual([paragraph(text('alert(1)  gras'))]);
  });

  it('drops raw HTML that spans lines, as a block', () => {
    expect(parseMarkdown('<div\n  onclick="x">\nText\n</div>')).toEqual([paragraph(text('Text'))]);
  });

  it('keeps markup inside inline code as text', () => {
    expect(parseMarkdown('Scrie `<b>` pentru gras')).toEqual([
      paragraph(text('Scrie '), { type: 'code', text: '<b>' }, text(' pentru gras')),
    ]);
  });

  it('reads Windows line endings', () => {
    expect(parseMarkdown('Unu\r\nDoi\r\n\r\nTrei')).toEqual([
      paragraph(text('Unu'), { type: 'break' }, text('Doi')),
      paragraph(text('Trei')),
    ]);
  });

  it('leaves an unclosed mark as text', () => {
    expect(parseMarkdown('un **început fără sfârșit')).toEqual([
      paragraph(text('un **început fără sfârșit')),
    ]);
  });
});
