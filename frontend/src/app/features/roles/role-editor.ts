import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  linkedSignal,
  output,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatTooltip } from '@angular/material/tooltip';
import { RoleChanges, RoleDto } from './models';
import { permissionGroups } from './permission-groups';

/** The role being edited, or null for a new one. A new Draft starts the form over. */
export interface Draft {
  role: RoleDto | null;
}

/** The longest name the API takes (Role.MaxNameLength). */
export const ROLE_NAME_MAX = 100;

/**
 * A role's name and its permissions, ticked by area (docs/UI_UX_SPEC.md §7.7). The system role shows
 * locked, with every box ticked and disabled: the one place where a disabled control explains itself.
 */
@Component({
  selector: 'kb-role-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatCheckbox, MatTooltip],
  templateUrl: './role-editor.html',
  styleUrl: './role-editor.scss',
})
export class RoleEditor {
  readonly draft = input.required<Draft>();
  /** Every permission the API knows, in its order. */
  readonly available = input.required<readonly string[]>();
  readonly busy = input(false);
  /** A refusal from the API about the name, such as a name already taken. */
  readonly nameError = input<string | null>(null);

  readonly save = output<RoleChanges>();
  readonly remove = output();

  protected readonly role = computed(() => this.draft().role);
  protected readonly locked = computed(() => this.role()?.isSystem ?? false);
  protected readonly groups = computed(() => permissionGroups(this.available()));
  protected readonly lockedReason = $localize`Rolul de sistem nu se poate modifica`;
  protected readonly nameMax = ROLE_NAME_MAX;

  protected readonly name = linkedSignal(() => this.role()?.name ?? '');
  protected readonly ticked = linkedSignal(() => new Set(this.role()?.permissions ?? []));
  private readonly missingName = linkedSignal(() => {
    this.draft();
    return false;
  });
  protected readonly error = computed(() =>
    this.missingName() ? $localize`Scrie un nume.` : this.nameError(),
  );

  /** Whether leaving now would lose something. */
  readonly changed = computed(() => {
    const before = new Set(this.role()?.permissions ?? []);
    const now = this.ticked();
    return (
      this.name() !== (this.role()?.name ?? '') ||
      before.size !== now.size ||
      [...now].some((permission) => !before.has(permission))
    );
  });

  protected toggle(permission: string, on: boolean): void {
    this.ticked.update((ticked) => {
      const next = new Set(ticked);
      if (on) {
        next.add(permission);
      } else {
        next.delete(permission);
      }
      return next;
    });
  }

  protected undo(): void {
    this.name.set(this.role()?.name ?? '');
    this.ticked.set(new Set(this.role()?.permissions ?? []));
    this.missingName.set(false);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.busy() || this.locked()) {
      return;
    }
    const name = this.name().trim();
    this.missingName.set(name === '');
    if (name === '') {
      return;
    }

    // In the order the API lists them, then any it no longer lists, so nothing is dropped unseen.
    const ticked = this.ticked();
    const known = this.available().filter((permission) => ticked.has(permission));
    const unknown = [...ticked].filter((permission) => !this.available().includes(permission));
    this.save.emit({ name, permissions: [...known, ...unknown] });
  }
}
