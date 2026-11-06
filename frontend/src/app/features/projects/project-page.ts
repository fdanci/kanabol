import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { PERMISSIONS } from '../../core/auth/permissions';
import { Banner } from '../../shared/ui/banner';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { Segment, SegmentedNav } from '../../shared/ui/segmented-nav';
import { Skeleton } from '../../shared/ui/skeleton';
import { ProjectStore } from './project-store';

/**
 * A project (docs/UI_UX_SPEC.md §7.3): its name in the header, and a segmented control for its
 * views, Board, Listă, Membri, Rapoarte for owners and reports.view, and Setări for those who can
 * change it. The views share its ProjectStore, which this page provides.
 */
@Component({
  selector: 'kb-project-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ProjectStore],
  imports: [
    RouterOutlet,
    RouterLink,
    PageHeader,
    SegmentedNav,
    Banner,
    EmptyState,
    Skeleton,
    MatButton,
  ],
  templateUrl: './project-page.html',
  styleUrl: './project-page.scss',
})
export class ProjectPage {
  protected readonly store = inject(ProjectStore);
  private readonly auth = inject(AuthStore);
  private readonly title = inject(Title);
  private readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    {
      initialValue: '',
    },
  );
  private readonly navigated = toSignal(
    inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
  );

  protected readonly segmentsLabel = $localize`Vederile proiectului`;
  protected readonly missingTitle = $localize`Proiectul nu există`;
  protected readonly forbiddenTitle = $localize`Nu ai acces la acest proiect`;
  protected readonly loadFailed = $localize`Nu am putut încărca proiectul.`;

  protected readonly crumbs = computed<Crumb[]>(() => [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Proiecte`, link: '/projects' },
    { label: this.store.project()?.name ?? '' },
  ]);

  protected readonly segments = computed<Segment[]>(() => {
    const project = this.store.project();
    const segments: Segment[] = [
      { label: $localize`Board`, link: 'board' },
      { label: $localize`Listă`, link: 'list' },
      { label: $localize`Membri`, link: 'members' },
    ];
    if (project?.canManage || this.auth.has(PERMISSIONS.reportsView)) {
      segments.push({ label: $localize`Rapoarte`, link: 'reports' });
    }
    if (project?.canManage) {
      segments.push({ label: $localize`Setări`, link: 'settings' });
    }
    return segments;
  });

  constructor() {
    // Only the address starts a load: what load reads of the store mustn't start another one.
    effect(() => {
      const id = this.id();
      if (id !== '') {
        untracked(() => void this.store.load(id));
      }
    });
    // The router names the tab after the route at each navigation, so the project's name goes after it.
    effect(() => {
      this.navigated();
      const name = this.store.project()?.name;
      if (name) {
        this.title.setTitle($localize`${name}:name: · Kanabol`);
      }
    });
  }

  protected retry(): void {
    void this.store.load(this.id());
  }
}
