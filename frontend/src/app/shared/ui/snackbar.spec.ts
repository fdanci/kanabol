import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject } from 'rxjs';
import { Snackbar } from './snackbar';

describe('Snackbar', () => {
  let action: Subject<void>;
  let open: ReturnType<typeof vi.fn>;
  let snackbar: Snackbar;

  beforeEach(() => {
    action = new Subject<void>();
    open = vi.fn(() => ({ onAction: () => action.asObservable() }));
    TestBed.configureTestingModule({ providers: [{ provide: MatSnackBar, useValue: { open } }] });
    snackbar = TestBed.inject(Snackbar);
  });

  it('shows a message bottom left for 4 seconds', () => {
    snackbar.show('Salvat.');

    expect(open).toHaveBeenCalledWith(
      'Salvat.',
      undefined,
      expect.objectContaining({
        duration: 4000,
        horizontalPosition: 'start',
        verticalPosition: 'bottom',
      }),
    );
  });

  it('shows an error for 8 seconds with the last 6 characters of its id', () => {
    snackbar.error('Nu am putut salva.', 'b7e1c2d4-0000-4000-8000-00000a1b2c3d');

    expect(open).toHaveBeenCalledWith(
      'Nu am putut salva. Cod: 1b2c3d',
      'Copiază',
      expect.objectContaining({
        duration: 8000,
        horizontalPosition: 'start',
        verticalPosition: 'bottom',
      }),
    );
  });

  it('copies the whole id when asked', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

    snackbar.error('Nu am putut salva.', 'b7e1c2d4-0000-4000-8000-00000a1b2c3d');
    action.next();
    await Promise.resolve();

    expect(writeText).toHaveBeenCalledWith('b7e1c2d4-0000-4000-8000-00000a1b2c3d');
  });

  it('shows an error without an id as it is', () => {
    snackbar.error('Serverul nu răspunde.', null);

    expect(open).toHaveBeenCalledWith(
      'Serverul nu răspunde.',
      undefined,
      expect.objectContaining({ duration: 8000 }),
    );
  });
});
