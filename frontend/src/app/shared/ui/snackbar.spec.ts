import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatSnackBarHarness } from '@angular/material/snack-bar/testing';
import { ErrorSnack } from './error-snack';
import { Snackbar } from './snackbar';

const ID = 'b7e1c2d4-0000-4000-8000-00000a1b2c3d';

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Page {}

describe('Snackbar', () => {
  describe('what it asks Material for', () => {
    let open: ReturnType<typeof vi.fn>;
    let openFromComponent: ReturnType<typeof vi.fn>;

    beforeEach(() => {
      open = vi.fn();
      openFromComponent = vi.fn();
      TestBed.configureTestingModule({
        providers: [{ provide: MatSnackBar, useValue: { open, openFromComponent } }],
      });
    });

    it('shows a message bottom left for 4 seconds, on ink', () => {
      TestBed.inject(Snackbar).show('Salvat.');

      expect(open).toHaveBeenCalledWith(
        'Salvat.',
        undefined,
        expect.objectContaining({
          duration: 4000,
          horizontalPosition: 'start',
          verticalPosition: 'bottom',
          panelClass: 'kb-snack',
        }),
      );
    });

    it('shows an error for 8 seconds, in its own variant', () => {
      TestBed.inject(Snackbar).error('Nu am putut salva.', ID);

      expect(openFromComponent).toHaveBeenCalledWith(
        ErrorSnack,
        expect.objectContaining({
          duration: 8000,
          horizontalPosition: 'start',
          verticalPosition: 'bottom',
          panelClass: 'kb-snack',
          data: { message: 'Nu am putut salva.', correlationId: ID },
        }),
      );
    });
  });

  describe('the error variant', () => {
    async function shown(correlationId: string | null) {
      const fixture = TestBed.createComponent(Page);
      TestBed.inject(Snackbar).error('Nu am putut salva.', correlationId);
      const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
      return loader.getHarness(MatSnackBarHarness);
    }

    it('shows the message with the last 6 characters of the id', async () => {
      const snack = await shown(ID);

      expect(await (await snack.host()).text()).toContain('Nu am putut salva.');
      expect(await (await snack.host()).text()).toContain('Cod: 1b2c3d');
      expect(await snack.getActionDescription()).toBe('Copiază');
    });

    it('copies the whole id and goes away', async () => {
      const writeText = vi.fn(() => Promise.resolve());
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const snack = await shown(ID);

      await snack.dismissWithAction();

      expect(writeText).toHaveBeenCalledWith(ID);
      expect(await snack.isDismissed()).toBe(true);
    });

    it('has neither code nor action without an id', async () => {
      const snack = await shown(null);

      expect(await (await snack.host()).text()).not.toContain('Cod:');
      expect(await (await snack.host()).text()).toContain('Nu am putut salva.');
      expect(await snack.hasAction()).toBe(false);
    });
  });
});
