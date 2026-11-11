import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { EstimateSheet, MoveQuestion } from './ui/estimate-sheet';
import { PauseSheet } from './ui/pause-sheet';
import { SHEET_WIDTH } from './ui/sheet';

/**
 * What a move into În lucru or În pauză asks for first (docs/SPEC.md §4.4, docs/UI_UX_SPEC.md §7.4 and §7.5):
 * the board and the task's panel ask the same sheets. Each answers null when the person gave up.
 */
@Injectable({ providedIn: 'root' })
export class MoveQuestions {
  private readonly dialog = inject(MatDialog);

  /** The estimate in minutes, for a task about to start without one. */
  async estimate(key: string): Promise<number | null> {
    const ref = this.dialog.open<EstimateSheet, MoveQuestion, number>(EstimateSheet, {
      data: { key },
      width: SHEET_WIDTH.confirm,
      ariaLabelledBy: 'kb-sheet-title',
    });
    return (await firstValueFrom(ref.afterClosed())) ?? null;
  }

  /** Why the task goes on pause. */
  async pauseReason(key: string): Promise<string | null> {
    const ref = this.dialog.open<PauseSheet, MoveQuestion, string>(PauseSheet, {
      data: { key },
      width: SHEET_WIDTH.confirm,
      ariaLabelledBy: 'kb-sheet-title',
    });
    return (await firstValueFrom(ref.afterClosed())) ?? null;
  }
}
