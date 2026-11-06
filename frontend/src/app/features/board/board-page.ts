import { CdkDragDrop, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { TaskChanges } from '../../shared/task-changes';
import { openTaskPanel } from '../../shared/task-panel';
import { EmptyState } from '../../shared/ui/empty-state';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { BoardColumn, NewCard } from './board-column';
import { placeAmong } from './board-moves';
import { BoardStore } from './board-store';
import { BoardColumnDto, BoardTaskDto } from './models';

/**
 * The kanban board of a project (docs/UI_UX_SPEC.md §7.4, docs/SPEC.md §6.3): its columns side by
 * side, scrolling sideways, and cards that move by drag and drop between and within them. A card
 * opens its panel over the board; what the panel saves reads the board again.
 */
@Component({
  selector: 'kb-board-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BoardStore],
  imports: [CdkDropListGroup, BoardColumn, EmptyState, Skeleton, MatButton],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPage {
  protected readonly store = inject(BoardStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly changes = inject(TaskChanges);
  private readonly snackbar = inject(Snackbar);

  protected readonly loadFailed = $localize`Nu am putut încărca board-ul.`;
  protected readonly boardLabel = $localize`Board`;

  private readonly projectId = toSignal(
    (this.route.parent ?? this.route).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: '' },
  );

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      if (projectId !== '') {
        untracked(() => {
          this.store.open(projectId);
        });
      }
    });
    // What the panel saved: the board reads itself again, past its first read.
    let seen = this.changes.version();
    effect(() => {
      const version = this.changes.version();
      if (version !== seen) {
        seen = version;
        untracked(() => {
          this.store.reload();
        });
      }
    });
  }

  protected open(task: BoardTaskDto): void {
    void openTaskPanel(this.router, task.key);
  }

  /**
   * The CDK gives the place among the cards the column shows, with the dragged one out of them; the
   * API wants the place among all the column's cards, which the filters may hide some of.
   */
  protected dropped(event: CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>): void {
    const task = event.item.data;
    const column = event.container.data;
    if (event.previousContainer === event.container && event.previousIndex === event.currentIndex) {
      return;
    }
    const others = this.store.cardsIn(column.id).filter((card) => card.id !== task.id);
    const position = placeAmong(others, others, event.currentIndex);
    void this.store.moveTask(task.id, column.id, position);
  }

  protected async create(card: NewCard): Promise<void> {
    const key = await this.store.createTask(card.column.id, card.title);
    if (key) {
      this.snackbar.show($localize`Task-ul ${key}:key: a fost creat.`);
    }
  }

  protected retry(): void {
    this.store.reload();
  }
}
