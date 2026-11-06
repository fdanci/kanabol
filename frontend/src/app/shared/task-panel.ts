import { ActivatedRoute, Router } from '@angular/router';

/**
 * The task panel has its own outlet next to the page, so a task opens over the board or the list it
 * was opened from, with its own address: /projects/p1/list(panel:task/KAN-12). Any feature opens it
 * through these, without knowing the tasks feature (docs/SPEC.md §6.1).
 */
export const TASK_PANEL_OUTLET = 'panel';

/** The route that holds the page and the panel: the shell, the first child of the root. */
function shell(router: Router): ActivatedRoute {
  return router.routerState.root.firstChild ?? router.routerState.root;
}

/** Keeps the page's query, such as the list's filters, so closing the panel finds them again. */
export function openTaskPanel(router: Router, key: string): Promise<boolean> {
  return router.navigate([{ outlets: { [TASK_PANEL_OUTLET]: ['task', key] } }], {
    relativeTo: shell(router),
    queryParamsHandling: 'preserve',
  });
}

export function closeTaskPanel(router: Router): Promise<boolean> {
  return router.navigate([{ outlets: { [TASK_PANEL_OUTLET]: null } }], {
    relativeTo: shell(router),
    queryParamsHandling: 'preserve',
  });
}

/** A page of a project with a task open over it, for /tasks/KAN-12. */
export function openTaskOver(
  router: Router,
  projectId: string,
  view: 'board' | 'list',
  key: string,
): Promise<boolean> {
  return router.navigate(
    [{ outlets: { primary: ['projects', projectId, view], [TASK_PANEL_OUTLET]: ['task', key] } }],
    { relativeTo: shell(router), replaceUrl: true },
  );
}
