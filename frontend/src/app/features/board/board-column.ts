import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { ColumnKind } from '../../shared/models/column-kind';
import { ArchivedTaskDto, BoardColumnDto, BoardTaskDto } from './models';
import { TaskCard } from './task-card';

/** A title as long as the API takes (docs/SPEC.md §5.1). */
const MAX_TITLE_LENGTH = 300;

/** A column's name as long as the API takes. */
const MAX_NAME_LENGTH = 100;

export interface NewCard {
  column: BoardColumnDto;
  title: string;
}

/** The fixed columns' icons (docs/UI_UX_SPEC.md §7.4), each drawn in its kind's color; the project's own have none. */
const KIND_ICONS: Partial<Record<ColumnKind, string>> = {
  Backlog: 'inbox',
  InProgress: 'play_circle',
  Paused: 'pause_circle',
  Done: 'check_circle',
};

/**
 * A column of the board (docs/UI_UX_SPEC.md §7.4): its name, after the icon of its kind for a fixed one,
 * with a bar of that color on its top edge, how many cards it has, the cards themselves as a CDK drop
 * list, and "+ Task" at the bottom where the page allows it, which opens a title field in place. For an
 * owner, a column of the project's own has a ⋮ that renames it in place, moves it as far as Backlog and
 * Gata let it, and deletes it when empty; the fixed ones have none. A column before În lucru takes no
 * card that already started. It shows the cards the page gives it, which the filters and a card picked
 * up from the keyboard decide. It says what happened; the board page decides what it means.
 */
@Component({
  selector: 'kb-board-column',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-column-id]': 'column().id' },
  imports: [
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    TaskCard,
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
  ],
  template: `
    <section
      class="column"
      [class]="'kind-' + column().kind.toLowerCase()"
      [attr.aria-labelledby]="'kb-column-' + column().id"
    >
      <!-- The column drags by its head, a column of the project's own for canManageColumns (group 23). -->
      <header
        class="head"
        cdkDragHandle
        [cdkDragHandleDisabled]="!canDrag() || renaming()"
        [class.grab]="canDrag()"
      >
        @if (icon(); as icon) {
          <span class="material-symbols-outlined kind-icon" aria-hidden="true">{{ icon }}</span>
        }
        <!-- Kept while renaming, out of sight, so the column keeps its name for screen readers. -->
        <h2
          class="kb-subtitle name"
          [class.hidden-name]="renaming()"
          [id]="'kb-column-' + column().id"
        >
          {{ column().name }}
        </h2>
        @if (renaming()) {
          <input
            #renameField
            class="kb-field rename"
            type="text"
            [maxLength]="maxNameLength"
            [value]="column().name"
            i18n-aria-label
            aria-label="Numele coloanei"
            (keydown.enter)="rename(renameField)"
            (keydown.escape)="renaming.set(false)"
            (blur)="rename(renameField)"
          />
        }
        <span class="count" aria-hidden="true">{{ cards().length }}</span>
        <span class="kb-visually-hidden">{{ countLabel() }}</span>
        @if (canManageColumns() && column().kind === 'Custom') {
          <button
            matIconButton
            class="menu"
            type="button"
            [matMenuTriggerFor]="actions"
            [attr.aria-label]="menuLabel()"
          >
            <mat-icon>more_vert</mat-icon>
          </button>
          <mat-menu #actions="matMenu" class="kb-menu" xPosition="before">
            <button mat-menu-item type="button" (click)="startRenaming()">
              <mat-icon>edit</mat-icon>
              <span i18n>Redenumește</span>
            </button>
            @if (canMoveLeft()) {
              <button mat-menu-item type="button" (click)="moved.emit(-1)">
                <mat-icon>arrow_back</mat-icon>
                <span i18n>Mută la stânga</span>
              </button>
            }
            @if (canMoveRight()) {
              <button mat-menu-item type="button" (click)="moved.emit(1)">
                <mat-icon>arrow_forward</mat-icon>
                <span i18n>Mută la dreapta</span>
              </button>
            }
            @if (canDelete()) {
              <button mat-menu-item class="kb-menu-danger" type="button" (click)="deleted.emit()">
                <mat-icon>delete</mat-icon>
                <span i18n>Șterge</span>
              </button>
            }
          </mat-menu>
        }
      </header>

      <div
        class="cards"
        cdkDropList
        [cdkDropListData]="column()"
        [cdkDropListDisabled]="!canMove()"
        [cdkDropListEnterPredicate]="accepts"
        (cdkDropListDropped)="dropped.emit($event)"
      >
        @for (task of cards(); track task.id) {
          <kb-task-card
            cdkDrag
            [cdkDragData]="task"
            [cdkDragDisabled]="!canMove()"
            [task]="task"
            [kind]="column().kind"
            [done]="column().kind === 'Done'"
            [pulse]="pulses().get(task.id) ?? 0"
            [class.lifted]="task.id === liftedId()"
            [attr.data-task-id]="task.id"
            tabindex="0"
            role="button"
            [attr.aria-label]="cardLabel(task)"
            [attr.aria-describedby]="canMove() ? keysHelpId() : null"
            (click)="opened.emit(task)"
            (keydown.enter)="opened.emit(task)"
            (cdkDragStarted)="dragStarted.emit()"
            (cdkDragEnded)="dragEnded.emit()"
          />
        }
      </div>

      @if (adding()) {
        <input
          #newTitle
          class="kb-field new-title"
          type="text"
          [maxLength]="maxTitleLength"
          i18n-placeholder
          placeholder="Titlul task-ului"
          i18n-aria-label
          aria-label="Titlul task-ului nou"
          (keydown.enter)="add(newTitle)"
          (keydown.escape)="adding.set(false)"
          (blur)="newTitle.value.trim() === '' && adding.set(false)"
        />
      } @else if (canCreate()) {
        <button matButton type="button" class="add kb-regular" (click)="adding.set(true)">
          <mat-icon>add</mat-icon>
          <ng-container i18n>Task</ng-container>
        </button>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
      flex: none;
      width: 300px;
    }

    .column {
      --kb-kind: transparent;
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
      max-height: var(--kb-board-height, calc(100vh - 248px));
      padding: 10px;
      border-radius: var(--kb-radius-md);
      background: color-mix(in srgb, var(--kb-navy-900) 4%, var(--kb-surface-50));
      /* The fixed kinds' 3px bar on the top edge, inside the column's corners. */
      box-shadow: inset 0 3px 0 var(--kb-kind);
    }

    .kind-backlog {
      --kb-kind: var(--kb-text-500);
    }

    .kind-inprogress {
      --kb-kind: var(--kb-accent-600);
    }

    .kind-paused {
      --kb-kind: var(--kb-warning-600);
    }

    .kind-done {
      --kb-kind: var(--kb-success-600);
    }

    .kind-icon {
      flex: none;
      font-size: 16px;
      color: var(--kb-kind);
    }

    .column:has(> .cards.cdk-drop-list-dragging) {
      outline: 2px solid var(--kb-accent-600);
      outline-offset: -2px;
    }

    .head {
      display: flex;
      align-items: center;
      gap: var(--kb-space-2);
      min-height: 28px;
      padding: 0 2px;
    }

    .head.grab {
      cursor: grab;
    }

    /* The column in hand (docs/UI_UX_SPEC.md §7.4, group 23): shadow-drag and 0.9 opacity. */
    :host(.cdk-drag-preview) {
      opacity: 0.9;
    }

    :host(.cdk-drag-preview) .column {
      box-shadow: var(--kb-shadow-drag);
    }

    :host(.cdk-drag-preview) .head.grab {
      cursor: grabbing;
    }

    .name {
      margin: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    /* While renaming: out of sight, still the column's name for screen readers. */
    .hidden-name {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }

    .rename {
      flex: 1;
      min-width: 0;
      box-sizing: border-box;
    }

    .menu {
      margin-left: auto;
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
      scrollbar-color: color-mix(in srgb, var(--kb-navy-900) 25%, transparent) transparent;
    }

    .cards.cdk-drop-list-dragging kb-task-card:not(.cdk-drag-placeholder) {
      transition: transform 200ms var(--kb-ease);
    }

    .add {
      align-self: flex-start;
    }

    .new-title {
      width: 100%;
      box-sizing: border-box;
    }

    @media (prefers-reduced-motion: reduce) {
      .cards.cdk-drop-list-dragging kb-task-card:not(.cdk-drag-placeholder) {
        transition: none;
      }
    }
  `,
})
export class BoardColumn {
  readonly column = input.required<BoardColumnDto>();
  readonly cards = input.required<BoardTaskDto[]>();
  /** How many times others changed each card, for its pulse. */
  readonly pulses = input<ReadonlyMap<string, number>>(new Map());
  readonly canMove = input(false);
  readonly canCreate = input(false);
  /** The card picked up from the keyboard, if any. */
  readonly liftedId = input<string | null>(null);
  /** What says how to move a card from the keyboard, for screen readers. */
  readonly keysHelpId = input<string | null>(null);

  readonly opened = output<BoardTaskDto>();
  readonly dropped = output<CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>>();
  /** A card was picked up, or let go, by the mouse: the board keeps still meanwhile. */
  readonly dragStarted = output();
  readonly dragEnded = output();
  readonly created = output<NewCard>();
  /** An owner, or projects.manage, of an open project. */
  /** The column's ⋮, for canManageColumns (docs/SPEC.md §4.4, group 23). */
  readonly canManageColumns = input(false);
  /** A column of the project's own, for canManageColumns: its head drags it (docs/UI_UX_SPEC.md §7.4). */
  readonly canDrag = input(false);
  /** Only an empty column of the project's own. */
  readonly canDelete = input(false);
  /** A column of the project's own, with room before Gata or after Backlog. */
  readonly canMoveLeft = input(false);
  readonly canMoveRight = input(false);
  /** Before În lucru: a card that already started doesn't come back here (docs/UI_UX_SPEC.md §7.4). */
  readonly beforeInProgress = input(false);
  /** The columns from În lucru on, where a task has started. */
  readonly startedColumnIds = input<ReadonlySet<string>>(new Set());
  readonly renamed = output<string>();
  /** -1 to the left, 1 to the right. */
  readonly moved = output<-1 | 1>();
  readonly deleted = output();

  protected readonly adding = signal(false);
  protected readonly icon = computed(() => KIND_ICONS[this.column().kind] ?? null);

  /** The key and the title, as on the card, and in În pauză its reason too (docs/UI_UX_SPEC.md §7.4). */
  protected cardLabel(task: BoardTaskDto): string {
    return this.column().kind === 'Paused' && task.pauseReason
      ? $localize`${task.key}:key: ${task.title}:title:. Pe pauză: ${task.pauseReason}:reason:`
      : `${task.key} ${task.title}`;
  }
  /**
   * Kept out while dragged, so the column takes no target border; the page says why when it's let go there.
   * A card of Arhivate comes in anywhere: it goes back to its own column.
   */
  protected readonly accepts = (drag: CdkDrag<BoardTaskDto | ArchivedTaskDto>): boolean =>
    'archivedAt' in drag.data ||
    !this.beforeInProgress() ||
    !this.startedColumnIds().has(drag.data.columnId);
  protected readonly maxTitleLength = MAX_TITLE_LENGTH;
  protected readonly maxNameLength = MAX_NAME_LENGTH;
  protected readonly renaming = signal(false);
  protected readonly menuLabel = computed(
    () => $localize`Acțiunile coloanei ${this.column().name}:name:`,
  );
  protected readonly countLabel = computed(() => $localize`${this.cards().length}:count: task-uri`);
  private readonly newTitle = viewChild<ElementRef<HTMLInputElement>>('newTitle');
  private readonly renameField = viewChild<ElementRef<HTMLInputElement>>('renameField');

  constructor() {
    // A field takes the focus as it opens, so choosing it and typing are one move.
    effect(() => {
      this.newTitle()?.nativeElement.focus();
    });
    effect(() => {
      const field = this.renameField()?.nativeElement;
      field?.focus();
      field?.select();
    });
  }

  protected startRenaming(): void {
    this.renaming.set(true);
  }

  /** Enter and leaving the field save; a blank or unchanged name changes nothing. */
  protected rename(field: HTMLInputElement): void {
    if (!this.renaming()) {
      return;
    }
    this.renaming.set(false);
    const name = field.value.trim();
    if (name !== '' && name !== this.column().name) {
      this.renamed.emit(name);
    }
  }

  /** The field stays open, empty, for the next card. */
  protected add(field: HTMLInputElement): void {
    const title = field.value.trim();
    if (title === '') {
      return;
    }
    this.created.emit({ column: this.column(), title });
    field.value = '';
  }
}
