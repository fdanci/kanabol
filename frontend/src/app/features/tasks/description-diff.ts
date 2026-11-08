import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DiffWordsOptionsAbortable, diffWordsWithSpace } from 'diff';

export interface DiffPart {
  text: string;
  kind: 'same' | 'added' | 'removed';
}

/**
 * Past this, two texts are too far apart to compare word by word without holding the page: a whole
 * rewrite of two descriptions of 40 kB would take seconds.
 */
const DIFF_TIMEOUT_MS = 500;

/**
 * Word by word, on the markdown source, as jsdiff gives it with the whitespace kept, so the lines of the
 * source stay where they were (docs/UI_UX_SPEC.md §7.5). When the comparison would take too long, the
 * old text is deleted whole and the new one added whole, which is still true.
 */
export function descriptionDiff(
  before: string | null,
  after: string | null,
  limit: DiffWordsOptionsAbortable = { timeout: DIFF_TIMEOUT_MS },
): DiffPart[] {
  const oldText = before ?? '';
  const newText = after ?? '';
  const changes = diffWordsWithSpace(oldText, newText, limit);
  if (!changes) {
    return [
      ...(oldText === '' ? [] : [{ text: oldText, kind: 'removed' as const }]),
      ...(newText === '' ? [] : [{ text: newText, kind: 'added' as const }]),
    ];
  }
  return changes.map((change) => ({
    text: change.value,
    kind: change.added ? 'added' : change.removed ? 'removed' : 'same',
  }));
}

/**
 * The diff of a description (docs/UI_UX_SPEC.md §7.5 "Diferența descrierii"): the markdown source as
 * text, 13px in the page's font, its lines kept, the deletions struck through on danger-bg, the
 * insertions on success-bg, the rest as it is. Nothing is rendered as markdown or HTML. The diff
 * package comes only with this component, in the tasks' chunk (docs/SPEC.md §2).
 */
@Component({
  selector: 'kb-description-diff',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Angular drops the whitespace-only text between these elements: only the texts' own shows.
  template: `<p class="diff">
    @for (part of parts(); track $index) {
      @switch (part.kind) {
        @case ('removed') {
          <del>{{ part.text }}</del>
        }
        @case ('added') {
          <ins>{{ part.text }}</ins>
        }
        @default {
          <span>{{ part.text }}</span>
        }
      }
    }
  </p>`,
  styles: `
    :host {
      display: block;
    }

    .diff {
      margin: 0;
      color: var(--kb-text-900);
      font: inherit;
      font-size: 13px;
      line-height: 20px;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    del {
      background: var(--kb-danger-bg);
      text-decoration: line-through;
    }

    ins {
      background: var(--kb-success-bg);
      text-decoration: none;
    }
  `,
})
export class DescriptionDiff {
  readonly before = input<string | null>(null);
  readonly after = input<string | null>(null);

  protected readonly parts = computed(() => descriptionDiff(this.before(), this.after()));
}
