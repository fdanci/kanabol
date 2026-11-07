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
import { Skeleton } from '../../shared/ui/skeleton';
import { historyLine } from './task-history';
import { TaskTimelineStore } from './task-timeline-store';

/**
 * The "Istoric" tab of a task's panel (docs/UI_UX_SPEC.md §7.5): a vertical timeline, an 8px accent
 * dot on each change, «**Florin** a schimbat *Prioritate*: Medium → High · acum 2 ore», from the
 * newest, 50 at a time with "Încarcă mai multe" at the end while there are more. The panel creates it
 * when the tab opens, so it reads then, and again each time revision moves: after a change the panel
 * saved. Nothing comes from SignalR (docs/SPEC.md §9).
 */
@Component({
  selector: 'kb-task-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TaskTimelineStore],
  imports: [MatButton, MatTooltip, RelativeTimePipe, Skeleton],
  template: `
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
                </p>
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
  protected readonly lines = computed(() =>
    this.store
      .entries()
      .map((entry) => ({ ...historyLine(entry), id: entry.id, changedAt: entry.changedAt })),
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
