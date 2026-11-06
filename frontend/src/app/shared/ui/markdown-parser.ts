/**
 * Basic markdown for task descriptions and comments (docs/UI_UX_SPEC.md §7.5): paragraphs, headings
 * of one to three marks, bold, italic, inline code, code blocks, lists, quotes, rules and links. It
 * reads text into a tree that `kb-markdown` renders through templates, so nothing it reads ever
 * becomes HTML: raw HTML is dropped, and links go only to http, https and mailto (docs/SPEC.md §16).
 */

export type Inline =
  | { type: 'text'; text: string }
  | { type: 'break' }
  | { type: 'strong'; children: Inline[] }
  | { type: 'em'; children: Inline[] }
  | { type: 'code'; text: string }
  | { type: 'link'; href: string; children: Inline[] };

export type Block =
  | { type: 'paragraph'; inlines: Inline[] }
  | { type: 'heading'; level: 1 | 2 | 3; inlines: Inline[] }
  | { type: 'list'; ordered: boolean; items: Inline[][] }
  | { type: 'code'; text: string }
  | { type: 'quote'; inlines: Inline[] }
  | { type: 'rule' };

/** Where a link may go: no javascript:, data: or anything else a browser would run. */
const SAFE_HREF = /^(https?:\/\/|mailto:)/i;

const FENCE = /^\s{0,3}```/;
const HEADING = /^\s{0,3}(#{1,3})\s+(.*?)\s*#*\s*$/;
const RULE = /^\s{0,3}([-*_])(\s*\1){2,}\s*$/;
const QUOTE = /^\s{0,3}>\s?(.*)$/;
const BULLET = /^\s{0,3}[-*+]\s+(.*)$/;
const NUMBERED = /^\s{0,3}\d{1,9}[.)]\s+(.*)$/;

/**
 * Inline code first, so what's in it stays text, then an autolink, which looks like a tag, then
 * comments and tags. The [^<>] lets a tag run over lines, as `<div\n onclick=…>` would.
 */
const RAW_HTML = /(`+)[\s\S]*?\1|<(https?:\/\/[^\s<>]+)>|<!--[\s\S]*?-->|<\/?[A-Za-z][^<>]*>/g;

/**
 * Inline code, a link, bold, italic with * or _, and an address written as is. An underscore inside
 * a word, as in task_items, isn't italic. An address doesn't take the punctuation that follows it.
 */
const INLINE = new RegExp(
  [
    String.raw`(\x60+)([^\x60]|[^\x60][\s\S]*?[^\x60])\1(?!\x60)`,
    String.raw`\[([^\[\]]+)\]\(([^()\s]*(?:\([^()\s]*\)[^()\s]*)*)\)`,
    String.raw`(\*\*|__)(?=\S)([\s\S]*?\S)\5`,
    String.raw`(?<![\w*])\*(?=[^\s*])([\s\S]*?[^\s*])\*(?!\*)`,
    String.raw`(?<![\p{L}\p{N}_])_(?=\S)([\s\S]*?\S)_(?![\p{L}\p{N}_])`,
    String.raw`(https?:\/\/[^\s<>]*[^\s<>.,;:!?'")\]])`,
  ].join('|'),
  'gu',
);

export function parseMarkdown(source: string): Block[] {
  const blocks: Block[] = [];
  for (const segment of fencedSegments(source.replace(/\r\n?/g, '\n'))) {
    if (segment.code) {
      blocks.push({ type: 'code', text: segment.text });
    } else {
      blocks.push(...parseBlocks(withoutRawHtml(segment.text)));
    }
  }
  return blocks;
}

/** Code blocks keep their text as it was written; an unclosed one runs to the end. */
function fencedSegments(source: string): { code: boolean; text: string }[] {
  const segments: { code: boolean; text: string }[] = [];
  let prose: string[] = [];
  let code: string[] | null = null;

  for (const line of source.split('\n')) {
    if (code === null && FENCE.test(line)) {
      segments.push({ code: false, text: prose.join('\n') });
      prose = [];
      code = [];
    } else if (code !== null && /^\s{0,3}```\s*$/.test(line)) {
      segments.push({ code: true, text: code.join('\n') });
      code = null;
    } else if (code !== null) {
      code.push(line);
    } else {
      prose.push(line);
    }
  }

  if (code !== null) {
    segments.push({ code: true, text: code.join('\n') });
  }
  segments.push({ code: false, text: prose.join('\n') });
  return segments.filter((segment) => segment.code || segment.text.trim() !== '');
}

function withoutRawHtml(text: string): string {
  return text.replace(RAW_HTML, (match: string, ticks?: string, address?: string) =>
    ticks ? match : (address ?? ''),
  );
}

function startsABlock(line: string): boolean {
  return [HEADING, RULE, QUOTE, BULLET, NUMBERED].some((pattern) => pattern.test(line));
}

function parseBlocks(text: string): Block[] {
  const lines = text.split('\n');
  const blocks: Block[] = [];
  let index = 0;

  while (index < lines.length) {
    const line = lines[index];
    if (line.trim() === '') {
      index++;
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({
        type: 'heading',
        level: heading[1].length as 1 | 2 | 3,
        inlines: parseInline(heading[2]),
      });
      index++;
      continue;
    }

    if (RULE.test(line)) {
      blocks.push({ type: 'rule' });
      index++;
      continue;
    }

    if (QUOTE.test(line)) {
      const quoted: string[] = [];
      let inner = QUOTE.exec(line);
      while (inner) {
        quoted.push(inner[1]);
        index++;
        inner = index < lines.length ? QUOTE.exec(lines[index]) : null;
      }
      blocks.push({ type: 'quote', inlines: linesToInlines(quoted) });
      continue;
    }

    const ordered = NUMBERED.test(line);
    if (ordered || BULLET.test(line)) {
      const marker = ordered ? NUMBERED : BULLET;
      const items: string[] = [];
      while (index < lines.length) {
        const item = marker.exec(lines[index]);
        if (item) {
          items.push(item[1]);
        } else if (/^\s+\S/.test(lines[index]) && !startsABlock(lines[index]) && items.length > 0) {
          items[items.length - 1] += ` ${lines[index].trim()}`;
        } else {
          break;
        }
        index++;
      }
      blocks.push({ type: 'list', ordered, items: items.map((item) => parseInline(item.trim())) });
      continue;
    }

    const paragraph: string[] = [];
    while (index < lines.length && lines[index].trim() !== '' && !startsABlock(lines[index])) {
      paragraph.push(lines[index]);
      index++;
    }
    blocks.push({ type: 'paragraph', inlines: linesToInlines(paragraph) });
  }

  return blocks;
}

/** One line break in the text is one in the page, as people expect in a task. */
function linesToInlines(lines: string[]): Inline[] {
  const inlines: Inline[] = [];
  lines
    .map((line) => line.trim())
    .filter((line) => line !== '')
    .forEach((line, position) => {
      if (position > 0) {
        inlines.push({ type: 'break' });
      }
      inlines.push(...parseInline(line));
    });
  return merged(inlines);
}

function parseInline(text: string): Inline[] {
  const inlines: Inline[] = [];
  let last = 0;

  for (const match of text.matchAll(INLINE)) {
    const start = match.index;
    if (start > last) {
      inlines.push({ type: 'text', text: text.slice(last, start) });
    }
    inlines.push(...inlineFrom(match));
    last = start + match[0].length;
  }

  if (last < text.length) {
    inlines.push({ type: 'text', text: text.slice(last) });
  }
  return merged(inlines);
}

/** The groups of INLINE, by number: only the alternative that matched has its own. */
const GROUP = { code: 2, linkText: 3, href: 4, strong: 6, star: 7, underscore: 8, address: 9 };

function inlineFrom(match: RegExpMatchArray): Inline[] {
  // TypeScript types every group as a string; the ones of the alternatives that didn't match are undefined.
  const group = (index: number): string | undefined => match[index];
  const code = group(GROUP.code);
  const linkText = group(GROUP.linkText);
  const strong = group(GROUP.strong);
  const emphasized = group(GROUP.star) ?? group(GROUP.underscore);

  if (code !== undefined) {
    return [{ type: 'code', text: code }];
  }
  if (linkText !== undefined) {
    const href = group(GROUP.href) ?? '';
    return SAFE_HREF.test(href)
      ? [{ type: 'link', href, children: parseInline(linkText) }]
      : [{ type: 'text', text: match[0] }];
  }
  if (strong !== undefined) {
    return [{ type: 'strong', children: parseInline(strong) }];
  }
  if (emphasized !== undefined) {
    return [{ type: 'em', children: parseInline(emphasized) }];
  }
  const address = group(GROUP.address) ?? '';
  return [{ type: 'link', href: address, children: [{ type: 'text', text: address }] }];
}

/** Text next to text is one piece, so a refused link reads as the words around it. */
function merged(inlines: Inline[]): Inline[] {
  const result: Inline[] = [];
  for (const inline of inlines) {
    const previous = result.at(-1);
    if (inline.type === 'text' && previous?.type === 'text') {
      result[result.length - 1] = { type: 'text', text: previous.text + inline.text };
    } else if (inline.type !== 'text' || inline.text !== '') {
      result.push(inline);
    }
  }
  return result;
}
