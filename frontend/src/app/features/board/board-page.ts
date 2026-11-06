import { LiveAnnouncer } from '@angular/cdk/a11y';
import { CdkDragDrop, CdkDropListGroup } from '@angular/cdk/drag-drop';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { map } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import {
  NO_TASK_FILTERS,
  TaskFilters,
  filtersFromQuery,
  filtersToQuery,
  hasFilters,
} from '../../shared/models/task-filters';
import { TaskChanges } from '../../shared/task-changes';
import { openTaskPanel } from '../../shared/task-panel';
import { EmptyState } from '../../shared/ui/empty-state';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskFilterBar } from '../../shared/ui/task-filter-bar';
import { BoardColumn, NewCard } from './board-column';
import { cardFilter } from './board-filters';
import { placeAmong } from './board-moves';
import { BoardStore } from './board-store';
import { BoardColumnDto, BoardTaskDto } from './models';

/** A card picked up from the keyboard: where it would land, among the cards that column shows. */
interface Lifted {
  taskId: string;
  key: string;
  columnId: string;
  index: number;
}

const MOVES: Record<string, [columns: number, places: number] | undefined> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
};

/**
 * The kanban board of a project (docs/UI_UX_SPEC.md §7.4, docs/SPEC.md §6.3): its columns side by
 * side, scrolling sideways, and cards that move by drag and drop, or from the keyboard, between and
 * within them. The filters above it live in the address and work on the cards it has. A card opens
 * its panel over the board; what the panel saves reads the board again.
 */
@Component({
  selector: 'kb-board-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BoardStore],
  imports: [CdkDropListGroup, BoardColumn, EmptyState, Skeleton, MatButton, TaskFilterBar],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPage {
  protected readonly store = inject(BoardStore);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly changes = inject(TaskChanges);
  private readonly snackbar = inject(Snackbar);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly loadFailed = $localize`Nu am putut încărca board-ul.`;
  protected readonly boardLabel = $localize`Board`;
  protected readonly keysHelpId = 'kb-board-keys';

  private readonly projectId = toSignal(
    (this.route.parent ?? this.route).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { initialValue: '' },
  );
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: convertToParamMap({}),
  });

  /** The board has no archived tasks, so that filter stays off here. */
  protected readonly filters = computed<TaskFilters>(() => ({
    ...filtersFromQuery(this.query()),
    archived: false,
  }));
  protected readonly filtered = computed(() => hasFilters(this.filters()));
  protected readonly people = computed(() =>
    this.store.members().filter((member) => member.isActive),
  );
  protected readonly lifted = signal<Lifted | null>(null);

  /**
   * The cards each column shows: those the filters let through, and the one picked up from the
   * keyboard where it would land.
   */
  protected readonly shown = computed(() => {
    const passes = cardFilter(
      this.filters(),
      this.store.columns(),
      this.auth.user()?.id ?? null,
      new Date(),
    );
    const lifted = this.lifted();
    const shown = new Map<string, BoardTaskDto[]>();
    for (const column of this.store.columns()) {
      shown.set(
        column.id,
        this.store.cardsIn(column.id).filter((task) => task.id !== lifted?.taskId && passes(task)),
      );
    }
    if (lifted) {
      const picked = this.store.board()?.tasks.find((task) => task.id === lifted.taskId);
      const target = shown.get(lifted.columnId);
      if (picked && target) {
        target.splice(Math.min(lifted.index, target.length), 0, picked);
      }
    }
    return shown;
  });
  protected readonly nothingShown = computed(
    () =>
      this.filtered() &&
      (this.store.board()?.tasks.length ?? 0) > 0 &&
      [...this.shown().values()].every((cards) => cards.length === 0),
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

  protected cardsOf(column: BoardColumnDto): BoardTaskDto[] {
    return this.shown().get(column.id) ?? [];
  }

  protected filter(filters: TaskFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: filtersToQuery({ ...filters, archived: false }),
      queryParamsHandling: 'merge',
    });
  }

  protected clearFilters(): void {
    this.filter(NO_TASK_FILTERS);
  }

  protected open(task: BoardTaskDto): void {
    if (!this.lifted()) {
      void openTaskPanel(this.router, task.key);
    }
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
    void this.store.moveTask(
      task.id,
      column.id,
      this.placeFor(task.id, column.id, event.currentIndex),
    );
  }

  /** docs/UI_UX_SPEC.md §7.4: Space picks up and lets go, the arrows move, Esc gives up. */
  protected keyed(event: KeyboardEvent): void {
    const card = (event.target as HTMLElement).closest<HTMLElement>('kb-task-card[data-task-id]');
    const taskId = card?.dataset['taskId'];
    if (!taskId || !this.store.canMove()) {
      return;
    }
    const lifted = this.lifted();
    if (event.key === ' ') {
      event.preventDefault();
      if (lifted) {
        void this.letGo(lifted);
      } else {
        this.pickUp(taskId);
      }
      return;
    }
    if (!lifted) {
      return;
    }
    const move = MOVES[event.key];
    if (move) {
      event.preventDefault();
      this.shift(lifted, ...move);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.lifted.set(null);
      void this.announcer.announce($localize`Mutarea a fost anulată.`);
      this.focus(lifted.taskId);
    }
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

  private pickUp(taskId: string): void {
    const task = this.store.board()?.tasks.find((found) => found.id === taskId);
    if (!task) {
      return;
    }
    const index = this.cardsOfId(task.columnId).findIndex((found) => found.id === taskId);
    this.lifted.set({ taskId, key: task.key, columnId: task.columnId, index: Math.max(0, index) });
    void this.announcer.announce(
      $localize`Ai ridicat «${task.key}:key:». Mută-l cu săgețile, lasă-l cu Space, renunță cu Esc.`,
    );
  }

  private shift(lifted: Lifted, columns: number, places: number): void {
    const order = this.store.columns();
    const from = order.findIndex((column) => column.id === lifted.columnId);
    const target = order[Math.max(0, Math.min(order.length - 1, from + columns))];
    const others = this.othersIn(target.id, lifted.taskId).length;
    const index = Math.max(0, Math.min(others, lifted.index + places));
    this.lifted.set({ ...lifted, columnId: target.id, index });
    void this.announcer.announce(
      $localize`«${target.name}:column:», poziția ${index + 1}:place: din ${others + 1}:count:`,
    );
    this.focus(lifted.taskId);
  }

  private async letGo(lifted: Lifted): Promise<void> {
    const column = this.store.columns().find((found) => found.id === lifted.columnId);
    if (!column) {
      return;
    }
    const count = this.othersIn(column.id, lifted.taskId).length + 1;
    const position = this.placeFor(lifted.taskId, column.id, lifted.index);
    this.lifted.set(null);
    const moved = await this.store.moveTask(lifted.taskId, column.id, position);
    void this.announcer.announce(
      moved
        ? $localize`Mutat în «${column.name}:column:», poziția ${lifted.index + 1}:place: din ${count}:count:`
        : $localize`Mutarea nu s-a salvat: task-ul a revenit la locul lui.`,
    );
    this.focus(lifted.taskId);
  }

  /** The place among all the column's cards for one put at shownIndex among the cards it shows. */
  private placeFor(taskId: string, columnId: string, shownIndex: number): number {
    const all = this.store.cardsIn(columnId).filter((card) => card.id !== taskId);
    return placeAmong(all, this.othersIn(columnId, taskId), shownIndex);
  }

  private othersIn(columnId: string, taskId: string): BoardTaskDto[] {
    return this.cardsOfId(columnId).filter((card) => card.id !== taskId);
  }

  private cardsOfId(columnId: string): BoardTaskDto[] {
    return this.shown().get(columnId) ?? [];
  }

  /** The card is drawn anew in its new column, so the focus follows it there once it is. */
  private focus(taskId: string): void {
    afterNextRender(
      () => {
        this.host.nativeElement
          .querySelector<HTMLElement>(`kb-task-card[data-task-id="${CSS.escape(taskId)}"]`)
          ?.focus();
      },
      { injector: this.injector },
    );
  }
}
