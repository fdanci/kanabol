import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import {
  ESTIMATE_STEP_HOURS,
  MAX_ESTIMATE_HOURS,
  MIN_ESTIMATE_HOURS,
  estimateProblem,
  hoursToMinutes,
  parseHours,
} from '../models/estimate';
import { Field } from './field';

/** The task a move asks about, by its key. */
export interface MoveQuestion {
  key: string;
}

/**
 * Before a task without an estimate starts (docs/UI_UX_SPEC.md §7.4): a 440px sheet asks for its estimate
 * in hours, 0.25 to 1000, with a comma or a dot, the arrows stepping 0.25. It closes with the estimate in
 * minutes, or with nothing on "Renunță", Esc or a click beside it, which put the card back.
 */
@Component({
  selector: 'kb-estimate-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton],
  template: `
    <form (submit)="submit($event)" novalidate>
      <h2 id="kb-sheet-title" class="kb-title" i18n>Cât estimezi pentru «{{ data.key }}»?</h2>
      <div class="kb-form-group">
        <div class="kb-form-row">
          <label class="kb-form-label kb-required" for="kb-estimate-hours" i18n
            >Estimare (ore)</label
          >
          <div>
            <input
              #hours
              id="kb-estimate-hours"
              class="kb-field hours"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              [value]="estimate.value()"
              [attr.aria-invalid]="estimate.error() !== null"
              [attr.aria-describedby]="
                estimate.error() !== null ? 'kb-estimate-error' : 'kb-estimate-help'
              "
              (input)="estimate.value.set(hours.value)"
              (blur)="estimate.touched.set(true)"
              (keydown.arrowup)="step(1, $event)"
              (keydown.arrowdown)="step(-1, $event)"
            />
            @if (estimate.error(); as message) {
              <p id="kb-estimate-error" class="kb-field-error">{{ message }}</p>
            } @else {
              <p id="kb-estimate-help" class="kb-form-help" i18n>
                Cât timp de lucru crezi că ia. O poți schimba din task.
              </p>
            }
          </div>
        </div>
      </div>
      <div class="actions">
        <button matButton="outlined" class="kb-bezel" type="button" (click)="ref.close()" i18n>
          Renunță
        </button>
        <button matButton="filled" class="kb-default" type="submit" i18n>Începe lucrul</button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
      padding: var(--kb-space-6);
    }

    h2 {
      margin: 0;
      overflow-wrap: anywhere;
    }

    .kb-form-group {
      margin-top: var(--kb-space-4);
    }

    .hours {
      max-width: 120px;
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--kb-space-2);
      margin-top: var(--kb-space-6);
    }
  `,
})
export class EstimateSheet {
  protected readonly data = inject<MoveQuestion>(MAT_DIALOG_DATA);
  protected readonly ref = inject<MatDialogRef<EstimateSheet, number>>(MatDialogRef);
  private readonly submitted = signal(false);
  protected readonly estimate = new Field('', estimateProblem, this.submitted);

  protected submit(event: Event): void {
    event.preventDefault();
    this.submitted.set(true);
    const hours = parseHours(this.estimate.value());
    if (this.estimate.error() === null && hours !== null) {
      this.ref.close(hoursToMinutes(hours));
    }
  }

  /** The arrows go a quarter of an hour up or down, within the limits. */
  protected step(direction: 1 | -1, event: Event): void {
    event.preventDefault();
    const now = parseHours(this.estimate.value()) ?? 0;
    const next =
      Math.round((now + direction * ESTIMATE_STEP_HOURS) / ESTIMATE_STEP_HOURS) *
      ESTIMATE_STEP_HOURS;
    const bounded = Math.min(MAX_ESTIMATE_HOURS, Math.max(MIN_ESTIMATE_HOURS, next));
    this.estimate.value.set(String(bounded).replace('.', ','));
  }
}
