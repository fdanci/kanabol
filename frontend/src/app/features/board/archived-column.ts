import { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuContent, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { ArchivedTaskDto, BoardColumnDto, BoardTaskDto } from './models';
import { TaskCard } from './task-card';

/** What the drop lists know the column Arhivate by: it isn't a column of the board (docs/SPEC.md §4.4). */
export const ARCHIVED_COLUMN: BoardColumnDto = {
  id: 'kb-archived',
  name: 'Arhivate',
  position: Number.MAX_SAFE_INTEGER,
  kind: 'Custom',
};

type ArchivedState = 'idle' | 'loading' | 'ready' | 'error';

/**
 * The column Arhivate (docs/UI_UX_SPEC.md §7.4), last on the board, for tasks.archive. Closed, a 44px band:
 * its icon, its name written downwards and how many tasks it holds, which a click or Enter opens. Open, 300px:
 * the archived cards, at 0.72 opacity, "arhivat acum 3 zile" in place of the deadline, 50 at a time with
 * "Încarcă mai multe", and "Niciun task arhivat." when there are none. A card of the board let go on it is to
 * be archived; one of its cards let go anywhere on the board is to be restored, which its ⋮ does from the
 * keyboard. The board's filters don't touch it. It says what happened; the board page decides what it means.
 */
@Component({
  selector: 'kb-archived-column',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CdkDropList,
    CdkDrag,
    TaskCard,
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuContent,
    MatMenuItem,
    MatMenuTrigger,
  ],
  host: { '[class.open]': 'open()', '[attr.data-column-id]': 'column.id' },
  template: `
    <section
      class="column"
      cdkDropList
      [cdkDropListData]="column"
      [cdkDropListEnterPredicate]="accepts"
      cdkDropListSortingDisabled
      [attr.aria-labelledby]="open() ? 'kb-column-archived' : null"
      (cdkDropListDropped)="dropped.emit($event)"
    >
      @if (!open()) {
        <button
          type="button"
          class="band"
          aria-expanded="false"
          [attr.aria-label]="bandLabel()"
          (click)="toggled.emit()"
        >
          <span class="material-symbols-outlined kind-icon" aria-hidden="true">inventory_2</span>
          <span class="band-name kb-subtitle" aria-hidden="true" i18n>Arhivate</span>
          <span class="count" aria-hidden="true">{{ total() }}</span>
        </button>
      } @else {
        <header class="head">
          <span class="material-symbols-outlined kind-icon" aria-hidden="true">inventory_2</span>
          <h2 class="kb-subtitle name" id="kb-column-archived" i18n>Arhivate</h2>
          <span class="count" aria-hidden="true">{{ total() }}</span>
          <span class="kb-visually-hidden">{{ countLabel() }}</span>
          <button
            matIconButton
            class="collapse"
            type="button"
            aria-expanded="true"
            i18n-aria-label
            aria-label="Strânge coloana Arhivate"
            (click)="toggled.emit()"
          >
            <mat-icon>left_panel_close</mat-icon>
          </button>
        </header>

        <div class="cards">
          @for (card of cards(); track card.id) {
            <div
              class="archived-card"
              cdkDrag
              [cdkDragData]="card"
              [cdkDragDisabled]="!canRestore()"
              [attr.data-task-id]="card.id"
            >
              <kb-task-card
                [task]="card"
                [archivedAt]="card.archivedAt"
                tabindex="0"
                role="button"
                [attr.aria-label]="card.key + ' ' + card.title"
                (click)="opened.emit(card)"
                (keydown.enter)="opened.emit(card)"
              />
              @if (canRestore()) {
                <button
                  matIconButton
                  class="card-menu"
                  type="button"
                  [matMenuTriggerFor]="actions"
                  [matMenuTriggerData]="{ card }"
                  [attr.aria-label]="menuLabel(card)"
                >
                  <mat-icon>more_vert</mat-icon>
                </button>
              }
            </div>
          } @empty {
            @if (state() === 'ready') {
              <p class="empty" i18n>Niciun task arhivat.</p>
            }
          }
        </div>

        @if (state() === 'error') {
          <p class="empty" i18n>Nu am putut încărca task-urile arhivate.</p>
          <button matButton type="button" class="kb-sm" (click)="retried.emit()" i18n>
            Reîncearcă
          </button>
        } @else if (hasMore()) {
          <button
            matButton
            type="button"
            class="kb-regular more"
            [disabled]="loadingMore()"
            (click)="more.emit()"
            i18n
          >
            Încarcă mai multe
          </button>
        }
      }
    </section>

    <mat-menu #actions="matMenu" class="kb-menu" xPosition="before">
      <ng-template matMenuContent let-card="card">
        <button mat-menu-item type="button" (click)="restored.emit(card)">
          <mat-icon>unarchive</mat-icon>
          <span i18n>Restaurează</span>
        </button>
      </ng-template>
    </mat-menu>
  `,
  styles: `
    :host {
      display: block;
      flex: none;
      width: 44px;
    }

    :host(.open) {
      width: 300px;
    }

    .column {
      --kb-kind: var(--kb-text-600);
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
      box-sizing: border-box;
      max-height: var(--kb-board-height, calc(100vh - 248px));
      height: 100%;
      padding: 10px;
      border-radius: var(--kb-radius-md);
      background: color-mix(in srgb, var(--kb-navy-900) 4%, var(--kb-surface-50));
      box-shadow: inset 0 3px 0 var(--kb-kind);
    }

    :host(:not(.open)) .column {
      padding: 0;
    }

    .column.cdk-drop-list-dragging,
    .column.cdk-drop-list-receiving {
      outline: 2px solid var(--kb-accent-600);
      outline-offset: -2px;
    }

    .band {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--kb-space-2);
      width: 44px;
      min-height: 160px;
      padding: 14px 0 10px;
      border: 0;
      border-radius: var(--kb-radius-md);
      background: transparent;
      color: var(--kb-text-900);
      font: inherit;
      cursor: pointer;
    }

    .band:focus-visible {
      outline: none;
      box-shadow: var(--kb-focus-ring);
    }

    .band-name {
      writing-mode: vertical-rl;
    }

    .head {
      display: flex;
      align-items: center;
      gap: var(--kb-space-2);
      min-height: 28px;
      padding: 0 2px;
    }

    .name {
      margin: 0;
    }

    .collapse {
      margin-left: auto;
    }

    .kind-icon {
      flex: none;
      font-size: 16px;
      color: var(--kb-kind);
    }

    .count {
      display: inline-grid;
      place-items: center;
      min-width: 22px;
      height: 22px;
      padding: 0 6px;
      box-sizing: border-box;
      border-radius: var(--kb-radius-pill);
      background: color-mix(in srgb, var(--kb-navy-900) 6%, var(--kb-surface-0));
      color: var(--kb-text-600);
      font-size: 12px;
      font-weight: 500;
    }

    .cards {
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
      min-height: 48px;
      overflow-y: auto;
      scrollbar-width: thin;
    }

    .archived-card {
      position: relative;
    }

    /* The ⋮ of a card, over its priority, shown on hover and while the card has the focus. */
    .card-menu {
      position: absolute;
      top: 4px;
      right: 4px;
      background: var(--kb-surface-0);
      opacity: 0;
    }

    .archived-card:hover .card-menu,
    .archived-card:focus-within .card-menu {
      opacity: 1;
    }

    .empty {
      margin: var(--kb-space-2) 2px;
      color: var(--kb-text-600);
      font-size: 13px;
    }

    .more {
      align-self: flex-start;
    }
  `,
})
export class ArchivedColumn {
  protected readonly column = ARCHIVED_COLUMN;

  readonly cards = input.required<ArchivedTaskDto[]>();
  readonly total = input(0);
  readonly open = input(false);
  readonly state = input<ArchivedState>('idle');
  readonly hasMore = input(false);
  readonly loadingMore = input(false);
  /** A real member of an open project archives a card by letting it go here. */
  readonly canArchive = input(false);
  /** An open project: its cards go back to the board. */
  readonly canRestore = input(false);

  readonly toggled = output();
  readonly more = output();
  readonly retried = output();
  readonly opened = output<ArchivedTaskDto>();
  readonly restored = output<ArchivedTaskDto>();
  readonly dropped = output<CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>>();

  protected readonly bandLabel = computed(
    () => $localize`Arhivate, ${this.total()}:count: task-uri. Deschide coloana.`,
  );
  protected readonly countLabel = computed(() => $localize`${this.total()}:count: task-uri`);

  /** Its own cards come back; the board's come in when they may be archived. */
  protected readonly accepts = (drag: CdkDrag<BoardTaskDto | ArchivedTaskDto>): boolean =>
    'archivedAt' in drag.data || this.canArchive();

  protected menuLabel(card: ArchivedTaskDto): string {
    return $localize`Acțiunile task-ului ${card.key}:key:`;
  }
}
