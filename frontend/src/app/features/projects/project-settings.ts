import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { Field } from '../../shared/ui/field';
import { Snackbar } from '../../shared/ui/snackbar';
import { ProjectDto } from './models';
import {
  PROJECT_DESCRIPTION_MAX,
  PROJECT_NAME_MAX,
  projectDescriptionProblem,
  projectNameProblem,
  refuseProjectFields,
} from './project-fields';
import { ProjectStore } from './project-store';

/**
 * The settings of a project (docs/UI_UX_SPEC.md §7.3): its name and description, its key, which
 * never changes, and archiving it or bringing it back. The columns come with the board.
 */
@Component({
  selector: 'kb-project-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, DatePipe],
  templateUrl: './project-settings.html',
  styleUrl: './project-settings.scss',
})
export class ProjectSettings {
  protected readonly store = inject(ProjectStore);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);

  protected readonly nameMax = PROJECT_NAME_MAX;
  protected readonly descriptionMax = PROJECT_DESCRIPTION_MAX;
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  protected readonly project = computed(() => this.store.project());

  // The form starts again from the project each time it changes, as after saving or restoring.
  protected readonly form = computed(() => {
    const project = this.project();
    return {
      name: new Field(project?.name ?? '', projectNameProblem, this.submitted),
      description: new Field(project?.description ?? '', projectDescriptionProblem, this.submitted),
    };
  });

  protected readonly changed = computed(
    () => this.form().name.changed() || this.form().description.changed(),
  );

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    const { name, description } = this.form();
    if (this.busy() || name.error() !== null || description.error() !== null) {
      return;
    }

    this.busy.set(true);
    try {
      const text = description.value().trim();
      const saved = await this.store.update({
        name: name.value().trim(),
        description: text === '' ? null : text,
      });
      this.submitted.set(false);
      this.snackbar.show($localize`Proiectul «${saved.name}:name:» a fost salvat.`);
    } catch (error) {
      if (!refuseProjectFields(error, { name, description })) {
        this.snackbar.error($localize`Proiectul nu a fost salvat. Reîncearcă.`, null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async archive(project: ProjectDto): Promise<void> {
    const sure = await this.confirmation.ask({
      title: $localize`Arhivezi proiectul «${project.name}:name:»?`,
      message: $localize`Dispare din lista de proiecte, dar rămâne cu tot ce are în el, iar un owner îl poate restaura oricând de aici.`,
      confirmLabel: $localize`Arhivează`,
      icon: 'archive',
      destructive: true,
    });
    if (!sure) {
      return;
    }
    await this.act(
      () => this.store.archive(),
      $localize`Proiectul «${project.name}:name:» a fost arhivat.`,
    );
  }

  protected restore(project: ProjectDto): Promise<void> {
    return this.act(
      () => this.store.restore(),
      $localize`Proiectul «${project.name}:name:» e din nou deschis.`,
    );
  }

  private async act(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.snackbar.show(done);
    } catch {
      // errorInterceptor has said why.
    } finally {
      this.busy.set(false);
    }
  }
}
