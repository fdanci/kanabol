import { Signal, WritableSignal, computed, linkedSignal, signal } from '@angular/core';

/**
 * One field of a sheet: its value, the problem the form finds in it once it was left or the form
 * sent, and the one the API found, until the value changes.
 */
export class Field {
  readonly value: WritableSignal<string>;
  readonly touched = signal(false);
  readonly refused: WritableSignal<string | null>;
  readonly error: Signal<string | null>;

  constructor(
    readonly initial: string,
    check: (value: string) => string | null,
    submitted: Signal<boolean>,
  ) {
    this.value = signal(initial);
    this.refused = linkedSignal({ source: this.value, computation: () => null });
    this.error = computed(
      () => this.refused() ?? (this.touched() || submitted() ? check(this.value()) : null),
    );
  }

  readonly changed = (): boolean => this.value() !== this.initial;
}
