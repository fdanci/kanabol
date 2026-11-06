import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { openTaskOver } from '../../shared/task-panel';
import { EmptyState } from '../../shared/ui/empty-state';
import { Skeleton } from '../../shared/ui/skeleton';
import { TasksApi } from './tasks-api';

type LinkState = 'loading' | 'missing' | 'forbidden' | 'error';

/**
 * /tasks/KAN-12, the address a task is shared by (docs/UI_UX_SPEC.md §7.5): it finds the task's
 * project and opens the task over the project's board, in place of this address.
 */
@Component({
  selector: 'kb-task-link-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, Skeleton, MatButton, RouterLink],
  template: `
    @switch (state()) {
      @case ('missing') {
        <kb-empty-state icon="search_off" [title]="missingTitle">
          <p i18n>A fost arhivat sau mutat, ori adresa e greșită.</p>
          <a kbEmptyAction matButton="outlined" class="kb-bezel" routerLink="/projects" i18n
            >Înapoi la proiecte</a
          >
        </kb-empty-state>
      }
      @case ('forbidden') {
        <kb-empty-state icon="lock" [title]="forbiddenTitle">
          <p i18n>Cere unui owner al proiectului să te adauge în el.</p>
          <a kbEmptyAction matButton="outlined" class="kb-bezel" routerLink="/projects" i18n
            >Înapoi la proiecte</a
          >
        </kb-empty-state>
      }
      @case ('error') {
        <kb-empty-state icon="error" [title]="loadFailed">
          <p i18n>Verifică conexiunea și reîncearcă.</p>
          <button
            kbEmptyAction
            matButton="outlined"
            class="kb-bezel"
            type="button"
            (click)="open()"
            i18n
          >
            Reîncearcă
          </button>
        </kb-empty-state>
      }
      @default {
        <div class="loading" aria-busy="true">
          <kb-skeleton width="240px" height="28px" />
          <kb-skeleton width="160px" />
        </div>
      }
    }
  `,
  styles: `
    .loading {
      display: grid;
      gap: var(--kb-space-3);
      padding: var(--kb-space-6);
    }
  `,
})
export class TaskLinkPage {
  private readonly api = inject(TasksApi);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly key = inject(ActivatedRoute).snapshot.paramMap.get('key') ?? '';

  protected readonly state = signal<LinkState>('loading');
  protected readonly missingTitle = $localize`Task-ul nu există`;
  protected readonly forbiddenTitle = $localize`Nu ai acces la acest task`;
  protected readonly loadFailed = $localize`Nu am putut încărca task-ul.`;

  constructor() {
    void this.open();
  }

  protected async open(): Promise<void> {
    this.state.set('loading');
    try {
      const task = await firstValueFrom(
        this.api.byKey(this.key, this.auth.has(PERMISSIONS.tasksArchive)),
      );
      await openTaskOver(this.router, task.projectId, 'board', task.key);
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.state.set(status === 404 ? 'missing' : status === 403 ? 'forbidden' : 'error');
    }
  }
}
