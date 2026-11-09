import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import {
  MatCell,
  MatCellDef,
  MatColumnDef,
  MatHeaderCell,
  MatHeaderCellDef,
  MatHeaderRow,
  MatHeaderRowDef,
  MatRow,
  MatRowDef,
  MatTable,
} from '@angular/material/table';
import { MatTooltip } from '@angular/material/tooltip';
import { TaskSort, TaskSortField } from '../../shared/models/task-filters';
import { RelativeTimePipe, fullDate } from '../../shared/pipes/relative-time-pipe';
import { AvatarStack } from '../../shared/ui/avatar-stack';
import { Chip } from '../../shared/ui/chip';
import { DeadlineChip } from '../../shared/ui/deadline-chip';
import { TaskSummaryDto, priorityLabel } from './models';
import { providePaginatorLabels } from '../../shared/ui/paginator-labels';

/** A row of a task list: the project's, or "Task-urile mele", which add the project's name. */
export type TaskRow = TaskSummaryDto & { projectName?: string };

/**
 * The table of the List view (docs/UI_UX_SPEC.md §7.3), which "Task-urile mele" show too (§7.2) with a
 * "Proiect" column: the columns asked for, headers that sort through buttons of their own with a 12px
 * chevron (§6.4), rows that open the task, and the paginator under it. The page holds the address.
 */
@Component({
  selector: 'kb-task-table',
  // Its own Romanian labels, so the paginator stays out of the first bundle (docs/SPEC.md §6.2).
  providers: [providePaginatorLabels()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatPaginator,
    MatTooltip,
    MatTable,
    MatColumnDef,
    MatHeaderCell,
    MatHeaderCellDef,
    MatCell,
    MatCellDef,
    MatHeaderRow,
    MatHeaderRowDef,
    MatRow,
    MatRowDef,
    RelativeTimePipe,
    AvatarStack,
    Chip,
    DeadlineChip,
  ],
  templateUrl: './task-table.html',
  styleUrl: './task-table.scss',
})
export class TaskTable {
  readonly label = input.required<string>();
  readonly rows = input.required<readonly TaskRow[]>();
  readonly columns = input.required<readonly string[]>();
  readonly sort = input.required<TaskSort>();
  readonly busy = input(false);
  readonly totalCount = input.required<number>();
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly pageSizes = input.required<number[]>();

  /** A header asked to sort by its column; the page decides the direction. */
  readonly sorted = output<TaskSortField>();
  readonly opened = output<TaskRow>();
  readonly paged = output<PageEvent>();

  protected readonly priorityLabel = priorityLabel;
  protected readonly fullDate = fullDate;
  protected readonly unassigned = $localize`Neasignat`;

  protected ariaSort(field: TaskSortField): 'ascending' | 'descending' | null {
    const sort = this.sort();
    if (sort.field !== field) {
      return null;
    }
    return sort.descending ? 'descending' : 'ascending';
  }
}
