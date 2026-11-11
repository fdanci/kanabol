import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { RefreshButton } from '../../shared/ui/refresh-button';
import { Skeleton } from '../../shared/ui/skeleton';
import { DescriptionDiff } from './description-diff';
import { historyLine } from './task-history';
import { TaskTimelineStore } from './task-timeline-store';

/**
 * The "Istoric" tab of a task's panel (docs/UI_UX_SPEC.md §7.5): a vertical timeline, an 8px accent
 * dot on each change, «**Florin** a schimbat *Prioritate*: Medium → High · acum 2 ore», from the
 * newest, 50 at a time with "Încarcă mai multe" at the end while there are more. The panel creates it
 * when the tab opens, so it reads then, and again each time revision moves: after a change the panel
 * saved. Nothing comes from SignalR (docs/SPEC.md §9). A change of the description offers "Vezi
 * diferența", which reads that entry with its texts and opens under the row a block of at most 240px
 * with the diff of the markdown source, word by word; "Ascunde" closes it.
 */
@Component({
  selector: 'kb-task-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TaskTimelineStore],
  imports: [DescriptionDiff, MatButton, MatTooltip, RefreshButton, RelativeTimePipe, Skeleton],
  template: `
    <div class="tab-head">
      <kb-refresh-button [busy]="store.reading()" (refresh)="reload()" />
    </div>
    @switch (store.state()) {
      @case ('loading') {
        <div class="loading" aria-busy="true">
          <kb-skeleton width="70%" />
          <kb-skeleton width="55%" />
        </div>
      }
      @case ('error') {
        <p class="empty" i18n>Nu am putut încărca istoricul.</p>
        <button matButton class="kb-sm" type="button" (click)="reload()" i18n>Reîncearcă</button>
      }
      @default {
        @if (lines().length === 0) {
          <p class="empty" i18n>Nicio schimbare încă.</p>
        } @else {
          <ol class="timeline">
            @for (line of lines(); track line.id) {
              <li class="entry">
                <span class="dot" aria-hidden="true"></span>
                <!-- &ngsp; keeps the spaces between the parts, which Angular drops between elements. -->
                <p class="line">
                  <strong>{{ line.who }}</strong
                  >&ngsp;<span class="what">
                    @switch (line.kind) {
                      @case ('description') {
                        <ng-container i18n>a schimbat descrierea</ng-container>
                      }
                      @case ('archived') {
                        <ng-container i18n>a arhivat task-ul</ng-container>
                      }
                      @case ('restored') {
                        <ng-container i18n>l-a restaurat</ng-container>
                      }
                      @case ('estimated') {
                        <ng-container i18n>a estimat task-ul la {{ line.change }}</ng-container>
                      }
                      @case ('paused') {
                        <ng-container i18n
                          >a pus task-ul pe pauză: «{{ line.change }}»</ng-container
                        >
                      }
                      @default {
                        <ng-container i18n
                          >a schimbat <em>{{ line.field }}</em
                          >: {{ line.change }}</ng-container
                        >
                      }
                    }</span
                  >&ngsp;<span class="when"
                    ><span aria-hidden="true">·</span>&ngsp;<time
                      [attr.datetime]="line.changedAt"
                      [matTooltip]="fullDate(line.changedAt)"
                      >{{ line.changedAt | relativeTime }}</time
                    ></span
                  >
                  @if (line.kind === 'description') {
                    &ngsp;<button
                      type="button"
                      class="diff-toggle"
                      [attr.aria-expanded]="line.diff !== null"
                      [attr.aria-controls]="'diff-' + line.id"
                      (click)="store.toggleDiff(line.id)"
                    >
                      @if (line.diff) {
                        <ng-container i18n>Ascunde</ng-container>
                      } @else {
                        <ng-container i18n>Vezi diferența</ng-container>
                      }
                    </button>
                  }
                </p>
                @if (line.diff; as diff) {
                  <div
                    class="diff-block"
                    [id]="'diff-' + line.id"
                    [attr.aria-busy]="diff.state === 'loading'"
                  >
                    @switch (diff.state) {
                      @case ('loading') {
                        <div class="loading">
                          <kb-skeleton width="80%" height="14px" />
                          <kb-skeleton width="60%" height="14px" />
                        </div>
                      }
                      @case ('error') {
                        <p class="empty" i18n>Nu am putut încărca diferența.</p>
                        <button
                          matButton
                          class="kb-sm"
                          type="button"
                          (click)="store.readDiff(line.id)"
                          i18n
                        >
                          Reîncearcă
                        </button>
                      }
                      @case ('ready') {
                        <kb-description-diff [before]="diff.before" [after]="diff.after" />
                      }
                    }
                  </div>
                }
              </li>
            }
          </ol>
          @if (store.hasMore()) {
            <button
              matButton="outlined"
              class="kb-bezel kb-regular more"
              type="button"
              [disabled]="store.loadingMore()"
              (click)="store.more()"
              i18n
            >
              Încarcă mai multe
            </button>
          }
        }
      }
    }
  `,
  styles: `
    /* The tab's first row: its "Reîncarcă" on the right (docs/UI_UX_SPEC.md §7.5). */
    .tab-head {
      display: flex;
      justify-content: flex-end;
      margin-bottom: var(--kb-space-1);
    }

    :host {
      display: block;
      padding: var(--kb-space-4) 0;
    }

    .timeline {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .entry {
      position: relative;
      padding: 0 0 var(--kb-space-4) var(--kb-space-6);
    }

    /* The line between the dots, from one change to the next. */
    .entry:not(:last-child)::before {
      content: '';
      position: absolute;
      top: 14px;
      bottom: -2px;
      left: 3px;
      width: 2px;
      background: var(--kb-border-200);
    }

    .dot {
      position: absolute;
      top: 6px;
      left: 0;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--kb-accent-600);
    }

    .line {
      margin: 0;
      color: var(--kb-text-700);
      font-size: 13px;
      line-height: 20px;
      overflow-wrap: anywhere;
    }

    strong {
      color: var(--kb-text-900);
      font-weight: 600;
    }

    em {
      font-style: italic;
    }

    .when {
      color: var(--kb-text-500);
      font-size: 12px;
    }

    .more {
      margin-left: var(--kb-space-6);
    }

    /* Looks like the app's links (styles.scss); a button, since it opens the diff in place. */
    .diff-toggle {
      padding: 0;
      border: 0;
      background: none;
      color: var(--kb-accent-600);
      font: inherit;
      font-size: 12px;
      cursor: pointer;
    }

    .diff-toggle:hover {
      text-decoration: underline;
    }

    /* At most 240px with its padding and border (UI spec §7.5); a longer diff scrolls inside. */
    .diff-block {
      box-sizing: border-box;
      max-height: 240px;
      overflow: auto;
      margin-top: var(--kb-space-2);
      padding: var(--kb-space-2) var(--kb-space-3);
      border: 1px solid var(--kb-border-200);
      border-radius: var(--kb-radius-sm);
      background: var(--kb-surface-0);
    }

    .empty {
      margin: 0;
      color: var(--kb-text-500);
      font-size: 13px;
    }

    .loading {
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
    }
  `,
})
export class TaskTimeline {
  readonly taskId = input.required<string>();
  /** The panel's count of saved changes: each one reads the history again. */
  readonly revision = input(0);

  protected readonly store = inject(TaskTimelineStore);
  protected readonly fullDate = fullDate;
  /** Each line, with its description's diff while it's open, null otherwise. */
  protected readonly lines = computed(() =>
    this.store.entries().map((entry) => {
      const line = historyLine(entry);
      const open = line.kind === 'description' && this.store.isOpen(entry.id);
      return {
        ...line,
        id: entry.id,
        changedAt: entry.changedAt,
        diff: open ? this.store.diff(entry.id) : null,
      };
    }),
  );

  constructor() {
    effect(() => {
      const taskId = this.taskId();
      this.revision();
      untracked(() => void this.store.load(taskId));
    });
  }

  protected reload(): void {
    void this.store.load(this.taskId());
  }
}
