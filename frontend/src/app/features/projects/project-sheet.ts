import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialogRef } from '@angular/material/dialog';
import { Confirmation, askBeforeClosing } from '../../shared/ui/confirm-dialog';
import { Field } from '../../shared/ui/field';
import { Snackbar } from '../../shared/ui/snackbar';
import { ProjectDto } from './models';
import {
  PROJECT_DESCRIPTION_MAX,
  PROJECT_KEY_MAX,
  PROJECT_NAME_MAX,
  asKey,
  projectDescriptionProblem,
  projectKeyProblem,
  projectNameProblem,
  refuseProjectFields,
  suggestKey,
} from './project-fields';
import { ProjectsStore } from './projects-store';

/**
 * A new project, in a 600px sheet (docs/UI_UX_SPEC.md §7.3): its name, the key of its tasks, which
 * starts from the name until the person types one, and a description. It closes with the project
 * as the API saved it, with its creator as owner and the default columns.
 */
@Component({
  selector: 'kb-project-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton],
  templateUrl: './project-sheet.html',
  styleUrl: './sheet.scss',
})
export class ProjectSheet {
  private readonly store = inject(ProjectsStore);
  private readonly ref = inject<MatDialogRef<ProjectSheet, ProjectDto>>(MatDialogRef);
  private readonly confirmation = inject(Confirmation);
  private readonly snackbar = inject(Snackbar);

  protected readonly nameMax = PROJECT_NAME_MAX;
  protected readonly keyMax = PROJECT_KEY_MAX;
  protected readonly descriptionMax = PROJECT_DESCRIPTION_MAX;
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);
  /** Once the person types a key, the name stops choosing it. */
  private readonly keyTyped = signal(false);

  protected readonly name = new Field('', projectNameProblem, this.submitted);
  protected readonly key = new Field('', projectKeyProblem, this.submitted);
  protected readonly description = new Field('', projectDescriptionProblem, this.submitted);

  private readonly fields = [this.name, this.key, this.description];
  private readonly changed = computed(() => this.fields.some((field) => field.changed()));

  constructor() {
    askBeforeClosing(this.ref, () => this.changed(), this.confirmation);
  }

  protected nameChanged(value: string): void {
    this.name.value.set(value);
    if (!this.keyTyped()) {
      this.key.value.set(suggestKey(value));
    }
  }

  protected keyChanged(input: HTMLInputElement): void {
    const key = asKey(input.value);
    // The field shows the key as it will be saved, in capitals and without other characters.
    input.value = key;
    this.keyTyped.set(key !== '');
    this.key.value.set(key);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.busy() || this.fields.some((field) => field.error() !== null)) {
      return;
    }

    this.busy.set(true);
    try {
      const description = this.description.value().trim();
      const saved = await this.store.create({
        name: this.name.value().trim(),
        key: this.key.value(),
        description: description === '' ? null : description,
      });
      this.ref.close(saved);
    } catch (error) {
      const explained = refuseProjectFields(error, {
        name: this.name,
        key: this.key,
        description: this.description,
      });
      if (!explained) {
        this.snackbar.error($localize`Proiectul nu a fost creat. Reîncearcă.`, null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.changed() || (await this.confirmation.discardChanges())) {
      this.ref.close();
    }
  }
}
