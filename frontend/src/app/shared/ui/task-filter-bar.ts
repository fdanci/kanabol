import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatOption } from '@angular/material/core';
import { MatSelect } from '@angular/material/select';
import {
  NO_TASK_FILTERS,
  PRIORITIES,
  TaskFilters,
  TaskPriority,
  hasFilters,
  priorityLabel,
} from '../models/task-filters';
import { Avatar } from './avatar';

/** How long the search waits for the typing to stop. */
export const SEARCH_DELAY_MS = 250;

export interface FilterPerson {
  userId: string;
  displayName: string;
}

/**
 * The filters above the board (docs/UI_UX_SPEC.md §7.4), which the List view shows too: a search in
 * the titles, Ale mele, Întârziate, one assignee and any priorities, and archived tasks for whoever
 * may see them. "Task-urile mele" (§7.2) leave the assignee out and add the finished ones. It only says
 * what changed; the page puts it in the address.
 */
@Component({
  selector: 'kb-task-filter-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatSelect, MatOption, MatButton, Avatar],
  template: `
    <div class="bar" role="group" i18n-aria-label aria-label="Filtre">
      <span class="search">
        <span class="material-symbols-outlined" aria-hidden="true">search</span>
        <input
          #searchInput
          type="search"
          i18n-placeholder
          placeholder="Caută în titluri"
          i18n-aria-label
          aria-label="Caută în titluri"
          [value]="search()"
          (input)="typed(searchInput.value)"
        />
        @if (search()) {
          <button
            type="button"
            class="cancel"
            i18n-aria-label
            aria-label="Șterge căutarea"
            (click)="typed('')"
          >
            <span class="material-symbols-outlined" aria-hidden="true">cancel</span>
          </button>
        }
      </span>

      @if (withAssignee()) {
        <button
          type="button"
          class="kb-filter-chip"
          [attr.aria-pressed]="filters().assignee === 'me'"
          (click)="toggleMine()"
          i18n
        >
          Ale mele
        </button>
      }
      <button
        type="button"
        class="kb-filter-chip"
        [attr.aria-pressed]="filters().overdue"
        (click)="toggleOverdue()"
        i18n
      >
        Întârziate
      </button>

      @if (withAssignee()) {
        <mat-select
          class="kb-field kb-select assignee"
          panelClass="kb-menu"
          i18n-aria-label
          aria-label="Asignat"
          [value]="filters().assignee ?? anyone"
          (selectionChange)="chooseAssignee($event.value)"
        >
          <mat-option [value]="anyone" i18n>Oricine</mat-option>
          <mat-option value="me" i18n>Ale mele</mat-option>
          @for (person of people(); track person.userId) {
            <mat-option [value]="person.userId">
              <span class="person">
                <kb-avatar [name]="person.displayName" />
                <span>{{ person.displayName }}</span>
              </span>
            </mat-option>
          }
        </mat-select>
      }

      <mat-select
        class="kb-field kb-select priority"
        panelClass="kb-menu"
        multiple
        i18n-placeholder
        placeholder="Prioritate"
        i18n-aria-label
        aria-label="Prioritate"
        [value]="filters().priorities"
        (selectionChange)="choosePriorities($event.value)"
      >
        @for (priority of priorities; track priority) {
          <mat-option [value]="priority">{{ priorityLabel(priority) }}</mat-option>
        }
      </mat-select>

      @if (finished() !== null) {
        <button
          type="button"
          class="kb-filter-chip"
          [attr.aria-pressed]="finished()"
          (click)="finishedChange.emit(!finished())"
          i18n
        >
          Și cele terminate
        </button>
      }

      @if (canSeeArchived()) {
        <button
          type="button"
          class="kb-filter-chip"
          [attr.aria-pressed]="filters().archived"
          (click)="toggleArchived()"
          i18n
        >
          Și cele arhivate
        </button>
      }

      @if (active()) {
        <button matButton class="kb-sm" type="button" (click)="clear()" i18n>
          Șterge filtrele
        </button>
      }
    </div>
  `,
  styles: `
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--kb-space-2);
    }

    /* The search pill of docs/UI_UX_SPEC.md §6.0.4. */
    .search {
      display: inline-flex;
      align-items: center;
      gap: var(--kb-space-1);
      height: 28px;
      padding: 0 var(--kb-space-2);
      border: 1px solid transparent;
      border-radius: var(--kb-radius-pill);
      background: color-mix(in srgb, var(--kb-navy-900) 5%, var(--kb-surface-0));
      color: var(--kb-text-600);

      &:focus-within {
        border-color: var(--kb-accent-600);
        box-shadow: var(--kb-focus-ring);
      }

      .material-symbols-outlined {
        font-size: 16px;
      }

      input {
        width: 180px;
        border: none;
        outline: none;
        background: transparent;
        color: var(--kb-text-900);
        font: inherit;
        font-size: 13px;

        &::-webkit-search-cancel-button {
          display: none;
        }
      }
    }

    .cancel {
      display: inline-grid;
      place-items: center;
      padding: 0;
      border: none;
      background: none;
      color: var(--kb-text-500);
      cursor: pointer;
    }

    .assignee,
    .priority {
      width: 160px;
    }

    .person {
      display: inline-flex;
      align-items: center;
      gap: var(--kb-space-2);
    }
  `,
})
export class TaskFilterBar {
  readonly filters = input.required<TaskFilters>();
  readonly people = input<FilterPerson[]>([]);
  readonly canSeeArchived = input(false);
  /** False where every task is the user's own, as on "Task-urile mele". */
  readonly withAssignee = input(true);
  /** The finished tasks too, on "Task-urile mele"; null leaves the chip out. */
  readonly finished = input<boolean | null>(null);
  readonly filtersChange = output<TaskFilters>();
  readonly finishedChange = output<boolean>();

  protected readonly priorities = PRIORITIES;
  protected readonly priorityLabel = priorityLabel;
  /** Not null: mat-select takes an option of null for no choice at all, and would show nothing. */
  protected readonly anyone = '';

  /** What's typed, ahead of the filters until the typing stops. */
  protected readonly search = linkedSignal(() => this.filters().search);
  protected readonly active = computed(() => hasFilters(this.filters()));
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.timer);
    });
  }

  protected typed(value: string): void {
    this.search.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.change({ search: value });
    }, SEARCH_DELAY_MS);
  }

  protected toggleMine(): void {
    this.change({ assignee: this.filters().assignee === 'me' ? null : 'me' });
  }

  protected toggleOverdue(): void {
    this.change({ overdue: !this.filters().overdue });
  }

  protected toggleArchived(): void {
    this.change({ archived: !this.filters().archived });
  }

  protected chooseAssignee(value: string): void {
    this.change({ assignee: value === this.anyone ? null : value });
  }

  protected choosePriorities(values: TaskPriority[]): void {
    this.change({ priorities: values });
  }

  protected clear(): void {
    clearTimeout(this.timer);
    this.search.set('');
    this.filtersChange.emit({ ...NO_TASK_FILTERS, archived: this.filters().archived });
  }

  private change(changes: Partial<TaskFilters>): void {
    this.filtersChange.emit({ ...this.filters(), ...changes });
  }
}
