import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDragPlaceholder,
  CdkDropList,
  CdkDropListGroup,
} from '@angular/cdk/drag-drop';
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
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
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
import { MoveQuestions } from '../../shared/move-questions';
import { TaskChanges } from '../../shared/task-changes';
import { openTaskPanel } from '../../shared/task-panel';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { TaskFilterBar } from '../../shared/ui/task-filter-bar';
import { ARCHIVED_COLUMN, ArchivedColumn } from './archived-column';
import { ArchivedStore } from './archived-store';
import { BoardColumn, NewCard } from './board-column';
import { cardFilter } from './board-filters';
import { crossesInProgress, placeAmong } from './board-moves';
import { BoardStore } from './board-store';
import { ArchivedTaskDto, BoardColumnDto, BoardTaskDto, MoveAnswers } from './models';

/** A card picked up from the keyboard: where it would land, among the cards that column shows. */
interface Lifted {
  taskId: string;
  key: string;
  columnId: string;
  index: number;
}

/** docs/UI_UX_SPEC.md §8: a card someone else moved slides to its place in the reordering's time. */
const MOVE_MS = 200;

/** The API's Task.CannotMoveBack, said before asking it (docs/UI_UX_SPEC.md §7.4). */
const NO_GOING_BACK = () => $localize`Un task început nu se mai întoarce înainte de «În lucru».`;

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
 * its panel over the board; what the panel saves reads the board again. What others change arrives
 * through the store: a card they moved slides to its new place, and every card they changed pulses.
 */
@Component({
  selector: 'kb-board-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BoardStore, ArchivedStore],
  imports: [
    CdkDropListGroup,
    CdkDropList,
    CdkDrag,
    CdkDragPlaceholder,
    BoardColumn,
    ArchivedColumn,
    EmptyState,
    Skeleton,
    MatButton,
    MatIcon,
    TaskFilterBar,
  ],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPage {
  protected readonly store = inject(BoardStore);
  protected readonly archived = inject(ArchivedStore);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly changes = inject(TaskChanges);
  private readonly snackbar = inject(Snackbar);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly confirmation = inject(Confirmation);
  private readonly questions = inject(MoveQuestions);
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
  /** Where În lucru is on the board: a task there or after it has started (docs/SPEC.md §4.4). */
  private readonly inProgressPosition = computed(
    () => this.store.columns().find((column) => column.kind === 'InProgress')?.position ?? 0,
  );
  protected readonly startedColumnIds = computed<ReadonlySet<string>>(
    () =>
      new Set(
        this.store
          .columns()
          .filter((column) => column.position >= this.inProgressPosition())
          .map((column) => column.id),
      ),
  );
  protected readonly addingColumn = signal(false);
  private readonly newColumn = viewChild<ElementRef<HTMLInputElement>>('newColumn');

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
    this.store.animateMovesWith((taskId, change) => {
      this.slide(taskId, change);
    });
    effect(() => {
      const projectId = this.projectId();
      if (projectId !== '') {
        untracked(() => {
          this.store.open(projectId);
        });
      }
    });
    // Arhivate, for tasks.archive: its cards are read once it opens, its count comes with the board.
    effect(() => {
      const projectId = this.projectId();
      if (projectId !== '' && this.store.canSeeArchived()) {
        untracked(() => {
          this.archived.show(projectId);
        });
      }
    });
    // The name field takes the focus as it opens.
    effect(() => {
      this.newColumn()?.nativeElement.focus();
    });
    // What the panel saved: the board reads itself again, past its first read.
    let seen = this.changes.version();
    effect(() => {
      const version = this.changes.version();
      if (version !== seen) {
        seen = version;
        untracked(() => {
          this.store.reload();
          if (this.store.canSeeArchived()) {
            void this.archived.read();
          }
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

  protected open(task: Pick<BoardTaskDto, 'key'>): void {
    if (!this.lifted()) {
      void openTaskPanel(this.router, task.key);
    }
  }

  /**
   * The CDK gives the place among the cards the column shows, with the dragged one out of them; the
   * API wants the place among all the column's cards, which the filters may hide some of.
   */
  protected dropped(event: CdkDragDrop<BoardColumnDto, BoardColumnDto, BoardTaskDto>): void {
    // Arhivate (docs/UI_UX_SPEC.md §7.4): a card of the board let go there is archived, after a question;
    // one of its cards let go anywhere on the board goes back to its own column.
    const fromArchived = event.previousContainer.data.id === ARCHIVED_COLUMN.id;
    if (event.container.data.id === ARCHIVED_COLUMN.id) {
      if (!fromArchived) {
        void this.archive(event.item.data);
      }
      return;
    }
    if (fromArchived) {
      void this.restore(event.item.data as unknown as ArchivedTaskDto);
      return;
    }
    const task = event.item.data;
    const column = event.container.data;
    // A column before În lucru keeps a started card out, so the CDK hands it back to where it was:
    // let go over such a column, it says why (docs/UI_UX_SPEC.md §7.4).
    const under = event.isPointerOverContainer ? null : this.columnUnder(event.dropPoint);
    if (this.goesBack(task, under ?? column)) {
      this.snackbar.show(NO_GOING_BACK());
      return;
    }
    if (event.previousContainer === event.container && event.previousIndex === event.currentIndex) {
      return;
    }
    void this.store.moveTask(
      task.id,
      column.id,
      this.placeFor(task.id, column.id, event.currentIndex),
      this.questionsFor(task, column),
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
      this.store.release();
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

  /** Only an empty column of the project's own, the hidden cards counting (docs/UI_UX_SPEC.md §7.4). */
  protected canDelete(column: BoardColumnDto): boolean {
    return column.kind === 'Custom' && this.store.cardsIn(column.id).length === 0;
  }

  /** A column of the project's own moves only between Backlog, always first, and Gata, always last. */
  protected canMoveLeft(column: BoardColumnDto): boolean {
    return column.kind === 'Custom' && column.position > 1;
  }

  protected canMoveRight(column: BoardColumnDto): boolean {
    return column.kind === 'Custom' && column.position < this.store.columns().length - 2;
  }

  protected beforeInProgress(column: BoardColumnDto): boolean {
    return column.position < this.inProgressPosition();
  }

  protected async addColumn(field: HTMLInputElement): Promise<void> {
    const name = field.value.trim();
    if (name !== '' && (await this.store.addColumn(name))) {
      this.addingColumn.set(false);
    }
  }

  protected renameColumn(column: BoardColumnDto, name: string): void {
    void this.store.renameColumn(column, name);
  }

  /** A column of the project's own drags by its head, for canManageColumns (docs/UI_UX_SPEC.md §7.4, group 23). */
  protected canDrag(column: BoardColumnDto): boolean {
    return column.kind === 'Custom' && this.store.canManageColumns();
  }

  /** Between Backlog, always first, and Gata, always last: the CDK keeps the place there while dragging. */
  protected readonly columnSlot = (index: number): boolean =>
    index > 0 && index < this.store.columns().length - 1;

  protected columnDropped(
    event: CdkDragDrop<BoardColumnDto[], BoardColumnDto[], BoardColumnDto>,
  ): void {
    if (event.previousIndex !== event.currentIndex) {
      void this.moveColumnTo(event.item.data, event.currentIndex);
    }
  }

  protected moveColumn(column: BoardColumnDto, by: -1 | 1): void {
    void this.moveColumnTo(column, column.position + by);
  }

  /**
   * Dragged or moved from its ⋮, the column takes its place at once and the screen reader hears where. One
   * holding cards doesn't cross În lucru (docs/UI_UX_SPEC.md §7.4, group 23): it stays, and a dialog says why,
   * before any request; one whose tasks the board doesn't show, archived ones, hears it from the API.
   */
  private async moveColumnTo(column: BoardColumnDto, position: number): Promise<void> {
    if (
      this.store.cardsIn(column.id).length > 0 &&
      crossesInProgress(this.store.columns(), column.id, position)
    ) {
      await this.tellColumnHasTasks(column);
      return;
    }
    const moved = await this.store.moveColumn(column, position);
    if (moved === 'moved') {
      const count = this.store.columns().length;
      void this.announcer.announce(
        $localize`«${column.name}:column:» mutată pe poziția ${position + 1}:place: din ${count}:count:.`,
      );
    } else if (moved === 'crossesInProgress') {
      await this.tellColumnHasTasks(column);
    }
  }

  private tellColumnHasTasks(column: BoardColumnDto): Promise<void> {
    return this.confirmation.tell({
      title: $localize`Coloana «${column.name}:column:» are task-uri`,
      message: $localize`O coloană cu task-uri nu poate trece peste «În lucru», pentru că un task început nu se mai întoarce înaintea ei. Mută întâi task-urile în altă coloană.`,
      confirmLabel: $localize`Am înțeles`,
      icon: 'info',
    });
  }

  protected async deleteColumn(column: BoardColumnDto): Promise<void> {
    const sure = await this.confirmation.ask({
      title: $localize`Ștergi coloana «${column.name}:column:»?`,
      message: $localize`E goală, așa că de pe board dispare doar ea.`,
      confirmLabel: $localize`Șterge`,
      icon: 'delete',
      destructive: true,
    });
    if (sure && (await this.store.deleteColumn(column))) {
      this.snackbar.show($localize`Coloana «${column.name}:column:» a fost ștearsă.`);
    }
  }

  protected async archive(task: BoardTaskDto): Promise<void> {
    const sure = await this.confirmation.ask({
      title: $localize`Arhivezi task-ul «${task.title}:title:»?`,
      message: $localize`Nu mai apare pe board și în listă, dar istoricul lui rămâne. Cine are permisiunea de arhivare îl poate restaura.`,
      confirmLabel: $localize`Arhivează`,
      icon: 'archive',
      destructive: true,
    });
    if (sure && (await this.archived.archive(task.id))) {
      this.store.reload();
      this.snackbar.show($localize`Task-ul ${task.key}:key: a fost arhivat.`);
    }
  }

  /** Back in its own column, wherever it was let go. */
  protected async restore(card: ArchivedTaskDto): Promise<void> {
    if (await this.archived.restore(card)) {
      this.store.reload();
      this.snackbar.show($localize`«${card.key}:key:» a revenit în «${card.columnName}:column:».`);
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
    this.store.hold();
    void this.announcer.announce(
      $localize`Ai ridicat «${task.key}:key:». Mută-l cu săgețile, lasă-l cu Space, renunță cu Esc.`,
    );
  }

  private shift(lifted: Lifted, columns: number, places: number): void {
    const order = this.store.columns();
    const from = order.findIndex((column) => column.id === lifted.columnId);
    const target = order[Math.max(0, Math.min(order.length - 1, from + columns))];
    const task = this.store.board()?.tasks.find((found) => found.id === lifted.taskId);
    // A started card stops at În lucru (docs/UI_UX_SPEC.md §7.4).
    if (task && this.goesBack(task, target)) {
      void this.announcer.announce(NO_GOING_BACK());
      return;
    }
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
    const task = this.store.board()?.tasks.find((found) => found.id === lifted.taskId);
    const ask = task ? this.questionsFor(task, column) : undefined;
    // Set from inside the question, which the compiler can't follow.
    const asked = { gaveUp: false };
    this.lifted.set(null);
    const moving = this.store.moveTask(
      lifted.taskId,
      column.id,
      position,
      ask &&
        (async () => {
          const answers = await ask();
          asked.gaveUp = answers === null;
          return answers;
        }),
    );
    this.store.release();
    const moved = await moving;
    void this.announcer.announce(
      moved
        ? $localize`Mutat în «${column.name}:column:», poziția ${lifted.index + 1}:place: din ${count}:count:`
        : asked.gaveUp
          ? $localize`Mutarea a fost anulată.`
          : $localize`Mutarea nu s-a salvat: task-ul a revenit la locul lui.`,
    );
    this.focus(lifted.taskId);
  }

  /**
   * What a move asks first (docs/UI_UX_SPEC.md §7.4): an estimate for a task entering În lucru without one,
   * a reason for every task entering În pauză; nothing for any other move.
   */
  private questionsFor(
    task: BoardTaskDto,
    column: BoardColumnDto,
  ): (() => Promise<MoveAnswers | null>) | undefined {
    if (column.id === task.columnId) {
      return undefined;
    }
    if (column.kind === 'InProgress' && task.estimatedMinutes === null) {
      return async () => {
        const minutes = await this.questions.estimate(task.key);
        return minutes === null ? null : { estimatedMinutes: minutes };
      };
    }
    if (column.kind === 'Paused') {
      return async () => {
        const reason = await this.questions.pauseReason(task.key);
        return reason === null ? null : { pauseReason: reason };
      };
    }
    return undefined;
  }

  /** A task in În lucru or after it, put in a column before it (docs/SPEC.md §4.4, Task.CannotMoveBack). */
  private goesBack(task: BoardTaskDto, target: BoardColumnDto): boolean {
    return this.startedColumnIds().has(task.columnId) && this.beforeInProgress(target);
  }

  /** The board's column under where a card was let go, if any. */
  private columnUnder(point: { x: number; y: number } | undefined): BoardColumnDto | null {
    if (!point || typeof document.elementFromPoint !== 'function') {
      return null;
    }
    const id = document
      .elementFromPoint(point.x, point.y)
      ?.closest<HTMLElement>('kb-board-column')
      ?.getAttribute('data-column-id');
    return this.store.columns().find((column) => column.id === id) ?? null;
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

  /**
   * A card someone else moved: where it was, the change, then from there to where it is now, in 200 ms
   * (docs/UI_UX_SPEC.md §7.4, §8). A copy of its box slides above the board, fixed to the window, since
   * a column would clip the card on its way from another and scroll sideways meanwhile; the card waits
   * under it, out of sight, and the copy goes when the slide ends, or a little after in a tab the browser
   * doesn't draw. Nothing moves for whoever asks for less motion, nor for a card the filters hide.
   */
  private slide(taskId: string, change: () => void): void {
    const before = this.cardBox(taskId)?.getBoundingClientRect();
    change();
    const lessMotion =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!before || lessMotion) {
      return;
    }
    afterNextRender(
      () => {
        const box = this.cardBox(taskId);
        if (!box || !('animate' in box)) {
          return;
        }
        const after = box.getBoundingClientRect();
        const dx = before.left - after.left;
        const dy = before.top - after.top;
        if (dx === 0 && dy === 0) {
          return;
        }
        const copy = box.cloneNode(true) as HTMLElement;
        copy.setAttribute('aria-hidden', 'true');
        Object.assign(copy.style, {
          position: 'fixed',
          left: `${String(after.left)}px`,
          top: `${String(after.top)}px`,
          width: `${String(after.width)}px`,
          height: `${String(after.height)}px`,
          margin: '0',
          boxSizing: 'border-box',
          pointerEvents: 'none',
          zIndex: '1000',
        });
        document.body.append(copy);
        box.style.visibility = 'hidden';
        const done = (): void => {
          copy.remove();
          box.style.visibility = '';
        };
        const ease = getComputedStyle(box).getPropertyValue('--kb-ease').trim() || 'ease';
        copy
          .animate(
            [{ transform: `translate(${String(dx)}px, ${String(dy)}px)` }, { transform: 'none' }],
            { duration: MOVE_MS, easing: ease },
          )
          .finished.then(done, done);
        setTimeout(done, MOVE_MS + 100);
      },
      { injector: this.injector },
    );
  }

  private cardBox(taskId: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(
      `kb-task-card[data-task-id="${CSS.escape(taskId)}"] .card`,
    );
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
