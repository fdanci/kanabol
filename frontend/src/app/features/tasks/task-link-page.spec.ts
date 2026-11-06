import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { TaskLinkPage } from './task-link-page';
import { aTask, refused } from './task-testing';
import { TasksApi } from './tasks-api';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet /><router-outlet name="panel" />`,
})
class ShellLike {}

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class Blank {}

// docs/UI_UX_SPEC.md §7.5: /tasks/KAN-123 opens the task over its project's board.
describe('TaskLinkPage', () => {
  let byKey: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    byKey = vi.fn(() => of(aTask({ projectId: 'p7', key: 'KAN-12' })));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: ShellLike,
            children: [
              { path: 'tasks/:key', component: TaskLinkPage },
              { path: 'projects/:id/board', component: Blank },
              { path: 'task/:key', outlet: 'panel', component: Blank },
            ],
          },
        ]),
        { provide: TasksApi, useValue: { byKey } },
        { provide: AuthStore, useValue: { has: () => false } },
      ],
    });
  });

  async function visit(url: string): Promise<RouterTestingHarness> {
    const harness = await RouterTestingHarness.create(url);
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      harness.fixture.detectChanges();
      await harness.fixture.whenStable();
    }
    return harness;
  }

  it('opens the task over its project board, in place of its own address', async () => {
    await visit('/tasks/kan-12');

    expect(byKey).toHaveBeenCalledWith('kan-12', false);
    expect(TestBed.inject(Router).url).toBe('/projects/p7/board(panel:task/KAN-12)');
  });

  it('says when no task has the key', async () => {
    byKey.mockReturnValueOnce(refused(404, 'Task.NotFound'));

    const harness = await visit('/tasks/KAN-99');

    expect(harness.routeNativeElement?.textContent).toContain('Task-ul nu există');
  });

  it('says when the task is in a project closed to the user', async () => {
    byKey.mockReturnValueOnce(refused(403, 'Project.MemberRequired'));

    const harness = await visit('/tasks/KAN-1');

    expect(harness.routeNativeElement?.textContent).toContain('Nu ai acces la acest task');
  });
});
