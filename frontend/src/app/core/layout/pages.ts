import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../../shared/ui/empty-state';

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
