import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ErrorSnack, ErrorSnackData } from './error-snack';

const PLACE = {
  horizontalPosition: 'start',
  verticalPosition: 'bottom',
  panelClass: 'kb-snack',
} as const;

/**
 * Feedback on an action, bottom left, one at a time: a new one replaces the one showing
 * (docs/UI_UX_SPEC.md §6.2). An error stays 8 seconds and shows the end of its correlation id.
 */
@Injectable({ providedIn: 'root' })
export class Snackbar {
  private readonly snackBar = inject(MatSnackBar);

  show(message: string): void {
    this.snackBar.open(message, undefined, { ...PLACE, duration: 4000 });
  }

  error(message: string, correlationId: string | null): void {
    this.snackBar.openFromComponent<ErrorSnack, ErrorSnackData>(ErrorSnack, {
      ...PLACE,
      duration: 8000,
      data: { message, correlationId },
    });
  }
}
