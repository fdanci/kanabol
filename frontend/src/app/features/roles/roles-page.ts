import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { problemIn } from '../../shared/models/problem-details';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { RoleChanges, RoleDto } from './models';
import { Draft, RoleEditor } from './role-editor';
import { RolesStore } from './roles-store';

/**
 * Roles (docs/UI_UX_SPEC.md §7.7): the list on the left, the selected role on the right, with its
 * permissions ticked by area. "Rol nou" opens an empty one in the same place.
 */
@Component({
  selector: 'kb-roles-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, MatButton, RoleEditor, EmptyState, Skeleton],
  templateUrl: './roles-page.html',
  styleUrl: './roles-page.scss',
})
export class RolesPage {
  protected readonly store = inject(RolesStore);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);
  private readonly editor = viewChild(RoleEditor);

  protected readonly heading = $localize`Roluri`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Roluri` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca rolurile.`;
  protected readonly noneSelected = $localize`Niciun rol ales`;

  protected readonly draft = signal<Draft | null>(null);
  protected readonly selectedId = computed(() => this.draft()?.role?.id ?? null);
  protected readonly busy = signal(false);
  protected readonly nameError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    await this.store.load();
    const first = this.store.roles().at(0);
    this.draft.set(first ? { role: first } : null);
  }

  protected async select(role: RoleDto): Promise<void> {
    if (role.id !== this.selectedId() && (await this.mayLeave())) {
      this.open({ role });
    }
  }

  protected async startNew(): Promise<void> {
    if (await this.mayLeave()) {
      this.open({ role: null });
    }
  }

  protected async save(changes: RoleChanges): Promise<void> {
    const existing = this.draft()?.role ?? null;
    this.busy.set(true);
    this.nameError.set(null);
    try {
      const saved = existing
        ? await this.store.update(existing.id, changes)
        : await this.store.create(changes);
      this.draft.set({ role: saved });
      this.snackbar.show(
        existing
          ? $localize`Rolul «${saved.name}:name:» a fost salvat.`
          : $localize`Rolul «${saved.name}:name:» a fost creat.`,
      );
    } catch (error) {
      this.explain(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const role = this.draft()?.role;
    if (!role) {
      return;
    }
    const sure = await this.confirmation.ask({
      title: $localize`Ștergi rolul «${role.name}:name:»?`,
      message: $localize`Rolul dispare de tot. Se poate șterge doar dacă nu-l mai are nimeni, nici un utilizator dezactivat.`,
      confirmLabel: $localize`Șterge`,
      icon: 'delete',
      destructive: true,
    });
    if (!sure) {
      return;
    }

    this.busy.set(true);
    try {
      await this.store.delete(role.id);
      this.draft.set(null);
      this.snackbar.show($localize`Rolul «${role.name}:name:» a fost șters.`);
    } catch {
      // errorInterceptor has said why, such as users still having the role.
    } finally {
      this.busy.set(false);
    }
  }

  private open(draft: Draft): void {
    this.nameError.set(null);
    this.draft.set(draft);
  }

  private async mayLeave(): Promise<boolean> {
    if (!this.editor()?.changed()) {
      return true;
    }
    return this.confirmation.ask({
      title: $localize`Renunți la modificări?`,
      message: $localize`Ce ai schimbat la acest rol nu a fost salvat și se pierde.`,
      confirmLabel: $localize`Renunță`,
      icon: 'undo',
      destructive: true,
    });
  }

  /** The changes stay in the form. errorInterceptor has shown anything that isn't about a field. */
  private explain(error: unknown): void {
    const problem = error instanceof HttpErrorResponse ? problemIn(error) : null;
    if (problem?.code === 'Role.NameTaken') {
      this.nameError.set($localize`Există deja un rol cu acest nume.`);
    } else if (problem?.errors?.['name']) {
      this.nameError.set($localize`Numele nu e bun: e prea lung sau are caractere care nu se văd.`);
    } else if (problem?.errors) {
      this.snackbar.error(
        $localize`Rolul nu a fost salvat. Reîncarcă pagina și reîncearcă.`,
        problem.correlationId ?? null,
      );
    }
  }
}
