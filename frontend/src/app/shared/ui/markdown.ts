import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Inline, parseMarkdown } from './markdown-parser';

/**
 * Basic markdown, rendered through templates from the tree `parseMarkdown` reads, never through
 * innerHTML, so what someone wrote can't become markup on another's page (docs/SPEC.md §16). Links
 * open in a new tab with rel="noopener noreferrer".
 */
@Component({
  selector: 'kb-markdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `
    @for (block of blocks(); track $index) {
      @switch (block.type) {
        @case ('heading') {
          @switch (block.level) {
            @case (1) {
              <h1>
                <ng-container *ngTemplateOutlet="inlines; context: { $implicit: block.inlines }" />
              </h1>
            }
            @case (2) {
              <h2>
                <ng-container *ngTemplateOutlet="inlines; context: { $implicit: block.inlines }" />
              </h2>
            }
            @default {
              <h3>
                <ng-container *ngTemplateOutlet="inlines; context: { $implicit: block.inlines }" />
              </h3>
            }
          }
        }
        @case ('paragraph') {
          <p><ng-container *ngTemplateOutlet="inlines; context: { $implicit: block.inlines }" /></p>
        }
        @case ('quote') {
          <blockquote>
            <ng-container *ngTemplateOutlet="inlines; context: { $implicit: block.inlines }" />
          </blockquote>
        }
        @case ('list') {
          @if (block.ordered) {
            <ol>
              @for (item of block.items; track $index) {
                <li><ng-container *ngTemplateOutlet="inlines; context: { $implicit: item }" /></li>
              }
            </ol>
          } @else {
            <ul>
              @for (item of block.items; track $index) {
                <li><ng-container *ngTemplateOutlet="inlines; context: { $implicit: item }" /></li>
              }
            </ul>
          }
        }
        @case ('code') {
          <pre><code>{{ block.text }}</code></pre>
        }
        @case ('rule') {
          <hr />
        }
      }
    }

    <ng-template #inlines let-nodes>
      @for (node of asInlines(nodes); track $index) {
        @switch (node.type) {
          @case ('text') {
            <!-- In its own container, so the indentation around it doesn't become spaces. -->
            <ng-container>{{ node.text }}</ng-container>
          }
          @case ('break') {
            <br />
          }
          @case ('strong') {
            <strong
              ><ng-container *ngTemplateOutlet="inlines; context: { $implicit: node.children }"
            /></strong>
          }
          @case ('em') {
            <em
              ><ng-container *ngTemplateOutlet="inlines; context: { $implicit: node.children }"
            /></em>
          }
          @case ('code') {
            <code>{{ node.text }}</code>
          }
          @case ('link') {
            <a [href]="node.href" target="_blank" rel="noopener noreferrer"
              ><ng-container *ngTemplateOutlet="inlines; context: { $implicit: node.children }"
            /></a>
          }
        }
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
      color: var(--kb-text-900);
      font-size: 13px;
      line-height: 1.55;
      overflow-wrap: anywhere;
    }

    h1,
    h2,
    h3 {
      margin: var(--kb-space-4) 0 var(--kb-space-2);
      font-weight: 600;
      line-height: 1.3;
    }

    h1 {
      font-size: 17px;
    }

    h2 {
      font-size: 15px;
    }

    h3 {
      font-size: 13px;
    }

    p,
    ul,
    ol,
    pre,
    blockquote {
      margin: 0 0 var(--kb-space-3);
    }

    :is(h1, h2, h3, p, ul, ol, pre, blockquote, hr):first-child {
      margin-top: 0;
    }

    ul,
    ol {
      padding-left: var(--kb-space-6);
    }

    code {
      padding: 1px 4px;
      border-radius: 4px;
      background: color-mix(in srgb, var(--kb-navy-900) 6%, var(--kb-surface-0));
      font-family: var(--kb-font-mono);
      font-size: 12px;
    }

    pre {
      padding: var(--kb-space-3);
      border-radius: var(--kb-radius-sm);
      background: color-mix(in srgb, var(--kb-navy-900) 6%, var(--kb-surface-0));
      overflow-x: auto;
    }

    pre code {
      padding: 0;
      background: none;
    }

    blockquote {
      padding-left: var(--kb-space-3);
      border-left: 3px solid var(--kb-border-300);
      color: var(--kb-text-600);
    }

    hr {
      border: none;
      border-top: 1px solid var(--kb-border-200);
      margin: var(--kb-space-4) 0;
    }

    a {
      color: var(--kb-accent-600);
    }
  `,
})
export class Markdown {
  readonly text = input('');

  protected readonly blocks = computed(() => parseMarkdown(this.text()));

  /** The template context is untyped; the nodes are always the parser's. */
  protected asInlines(nodes: unknown): Inline[] {
    return nodes as Inline[];
  }
}
