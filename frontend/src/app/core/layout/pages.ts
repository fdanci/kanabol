import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';

/** A page the sidebar links to, whose group in Tasks.md hasn't been built yet. */
@Component({
  selector: 'kb-placeholder-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState],
  template: `
    <kb-page-header [title]="title()" />
    <kb-empty-state class="content" icon="construction" i18n-title title="Pagina e în lucru">
      <p i18n>Apare aici într-o versiune viitoare.</p>
    </kb-empty-state>
  `,
  styles: `
    :host {
      display: block;
    }

    .content {
      margin: 0 var(--kb-space-6);
    }
  `,
})
export class PlaceholderPage {
  protected readonly title = toSignal(
    inject(ActivatedRoute).data.pipe(map((data) => (data['title'] as string | undefined) ?? '')),
    { initialValue: '' },
  );
}

/** Where permissionGuard sends a page opened without its permission, at the same address (UI §6.8). */
@Component({
  selector: 'kb-forbidden-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, MatButton, RouterLink],
  template: `
    <kb-empty-state icon="lock" i18n-title title="Nu ai acces la această pagină">
      <p i18n>Dacă ai nevoie de ea, cere-i accesul administratorului.</p>
      <a kbEmptyAction matButton="outlined" class="kb-bezel" routerLink="/" i18n>Panou principal</a>
    </kb-empty-state>
  `,
})
export class ForbiddenPage {}

@Component({
  selector: 'kb-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, MatButton, RouterLink],
  template: `
    <kb-empty-state icon="search_off" i18n-title title="Pagina nu există">
      <p i18n>Adresa e greșită sau pagina a fost mutată.</p>
      <a kbEmptyAction matButton="outlined" class="kb-bezel" routerLink="/" i18n>Panou principal</a>
    </kb-empty-state>
  `,
})
export class NotFoundPage {}
