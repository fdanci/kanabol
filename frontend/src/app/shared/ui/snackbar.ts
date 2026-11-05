import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

const PLACE = { horizontalPosition: 'start', verticalPosition: 'bottom' } as const;

/**
 * Feedback on an action, bottom left, one at a time: a new one replaces the one showing
 * (docs/UI_UX_SPEC.md §6.2). An error shows the end of its correlation id, and copies all of it.
 */
@Injectable({ providedIn: 'root' })
export class Snackbar {
  private readonly snackBar = inject(MatSnackBar);

  show(message: string): void {
    this.snackBar.open(message, undefined, { ...PLACE, duration: 4000 });
  }

  error(message: string, correlationId: string | null): void {
    if (!correlationId) {
      this.snackBar.open(message, undefined, { ...PLACE, duration: 8000 });
      return;
    }

    const shown = this.snackBar.open(
      `${message} ${$localize`Cod: ${correlationId.slice(-6)}:code:`}`,
      $localize`Copiază`,
      {
        ...PLACE,
        duration: 8000,
      },
    );
    shown.onAction().subscribe(() => {
      void navigator.clipboard.writeText(correlationId);
    });
  }
}
