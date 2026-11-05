import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthStore } from '../../core/auth/auth-store';
import { Permission } from '../../core/auth/permissions';

/**
 * `*kbHasPermission="'users.manage'"`: the element is there only for someone with the permission.
 * It tidies the UI and protects nothing, since the API checks every request (docs/SPEC.md §6.2).
 */
@Directive({ selector: '[kbHasPermission]' })
export class HasPermission {
  readonly kbHasPermission = input.required<Permission>();

  private readonly auth = inject(AuthStore);
  private readonly template = inject(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private shown = false;

  constructor() {
    effect(() => {
      const allowed = this.auth.has(this.kbHasPermission());
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
      } else if (!allowed && this.shown) {
        this.container.clear();
      }
      this.shown = allowed;
    });
  }
}
