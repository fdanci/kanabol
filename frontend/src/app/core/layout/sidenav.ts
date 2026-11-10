import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { navigation } from './navigation';

/**
 * The app's version, shown at the foot of the sidebar. The commit a version tags sets it, and the
 * default tag of compose.prod.yaml with it (docs/SPEC.md §13.5).
 */
export const APP_VERSION = '1.0.0';

/**
 * The source list (docs/UI_UX_SPEC.md §6.0.7): groups with a caption title and 28px items, the
 * selected one a tint-100 pill. Collapsed, it keeps only the icons, with their name in a tooltip.
 * What needs a permission the user lacks isn't there, nor a group left empty by it.
 */
@Component({
  selector: 'kb-sidenav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, MatTooltip],
  host: { '[class.collapsed]': 'collapsed()' },
  template: `
    <nav i18n-aria-label aria-label="Navigare">
      @for (group of groups(); track group.title) {
        <h2 class="title" [class.kb-visually-hidden]="collapsed()">{{ group.title }}</h2>
        <ul>
          @for (item of group.items; track item.link) {
            <li>
              <a
                [routerLink]="item.link"
                routerLinkActive="active"
                [routerLinkActiveOptions]="{ exact: item.link === '/' }"
                ariaCurrentWhenActive="page"
                [matTooltip]="item.label"
                matTooltipPosition="right"
                [matTooltipDisabled]="!collapsed()"
                (click)="navigated.emit()"
              >
                <span class="material-symbols-outlined" aria-hidden="true">{{ item.icon }}</span>
                <span class="label">{{ item.label }}</span>
              </a>
            </li>
          }
        </ul>
      }
    </nav>
    <footer class="version kb-caption" [class.kb-visually-hidden]="collapsed()" i18n>
      Versiunea {{ version }}
    </footer>
  `,
  styleUrl: './sidenav.scss',
})
export class Sidenav {
  readonly collapsed = input(false);
  readonly navigated = output();

  protected readonly version = APP_VERSION;
  private readonly auth = inject(AuthStore);

  protected readonly groups = computed(() =>
    navigation()
      .map((group) => ({
        ...group,
        items: group.items.filter((item) => !item.permission || this.auth.has(item.permission)),
      }))
      .filter((group) => group.items.length > 0),
  );
}
