import { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { BoardColumnDto, BoardTaskDto } from './models';
import { TaskCard } from './task-card';

/** A title as long as the API takes (docs/SPEC.md §5.1). */
const MAX_TITLE_LENGTH = 300;

export interface NewCard {
  column: BoardColumnDto;
  title: string;
}

/**
 * A column of the board (docs/UI_UX_SPEC.md §7.4): its name, a check when it's a done column, how
 * many cards it has, the cards themselves as a CDK drop list, and "+ Task" at the bottom, which
 * opens a title field in place. It shows the cards the page gives it, which the filters and a card
 * picked up from the keyboard decide. It says what happened; the board page decides what it means.
 */
@Component({
  selector: 'kb-board-column',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkDropList, CdkDrag, TaskCard, MatButton, MatIcon],
  template: `
    <section class="column" [attr.aria-labelledby]="'kb-column-' + column().id">
      <header class="head">
        <h3 class="kb-subtitle name" [id]="'kb-column-' + column().id">{{ column().name }}</h3>
        @if (column().isDone) {
          <span
            class="material-symbols-outlined done"
            role="img"
            i18n-aria-label
            aria-label="Coloană de task-uri terminate"
            >check_circle</span
          >
        }
        <span class="count" i18n-aria-label aria-label="{{ cards().length }} task-uri">{{
          cards().length
        }}</span>
      </header>

      <div
        class="cards"
        cdkDropList
        [cdkDropListData]="column()"
        [cdkDropListDisabled]="!canMove()"
        (cdkDropListDropped)="dropped.emit($event)"
      >
        @for (task of cards(); track task.id) {
          <kb-task-card
            cdkDrag
            [cdkDragData]="task"
            [cdkDragDisabled]="!canMove()"
            [task]="task"
            [done]="column().isDone"
            [class.lifted]="task.id === liftedId()"
            [attr.data-task-id]="task.id"
            tabindex="0"
            role="button"
            [attr.aria-label]="task.key + ' ' + task.title"
            [attr.aria-describedby]="canMove() ? keysHelpId() : null"
            (click)="opened.emit(task)"
            (keydown.enter)="opened.emit(task)"
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
      display: flex;
      flex-direction: column;
      gap: var(--kb-space-2);
      max-height: var(--kb-board-height, calc(100vh - 248px));
      padding: 10px;
      border-radius: var(--kb-radius-md);
      background: color-mix(in srgb, var(--kb-navy-900) 4%, var(--kb-surface-50));
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

    .name {
      margin: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .done {
      font-size: 16px;
      color: var(--kb-success-600);
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
  readonly canMove = input(false);
  readonly canCreate = input(false);
  /** The card picked up from the keyboard, if any. */
  readonly liftedId = input<string | null>(null);
  /** What says how to move a card from the keyboard, for screen readers. */
  readonly keysHelpId = input<string | null>(null);

  readonly opened = output<BoardTaskDto>();
  readonly dropped = output<CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>>();
  readonly created = output<NewCard>();

  protected readonly adding = signal(false);
  protected readonly maxTitleLength = MAX_TITLE_LENGTH;
  private readonly newTitle = viewChild<ElementRef<HTMLInputElement>>('newTitle');

  constructor() {
    // The field takes the focus as it opens, so "+ Task" and typing are one move.
    effect(() => {
      this.newTitle()?.nativeElement.focus();
    });
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
