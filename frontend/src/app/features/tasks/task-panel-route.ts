import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router } from '@angular/router';
import { closeTaskPanel } from '../../shared/task-panel';
import { TaskPanel, TaskPanelData, taskPanelConfig } from './task-panel';

/**
 * What the panel outlet shows, (panel:task/KAN-12): it opens the task's panel over the page, gives
 * it the key the address names, and closes the outlet when the panel closes. Leaving the address,
 * with the back button for one, closes the panel.
 */
@Component({
  selector: 'kb-task-panel-route',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
export class TaskPanelRoute {
  constructor() {
    const router = inject(Router);
    const key = signal('');
    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        key.set(params.get('key') ?? '');
      });

    const ref = inject(MatDialog).open<TaskPanel, TaskPanelData>(
      TaskPanel,
      taskPanelConfig({ key: key.asReadonly() }),
    );

    let leaving = false;
    ref.afterClosed().subscribe(() => {
      if (!leaving) {
        void closeTaskPanel(router);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      leaving = true;
      ref.close();
    });
  }
}
