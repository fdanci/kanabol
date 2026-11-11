import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialogHarness } from '@angular/material/dialog/testing';
import { MoveQuestions } from './move-questions';

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Page {}

/** docs/UI_UX_SPEC.md §7.4: the estimate's sheet and the pause's, which the board and the task's panel ask. */
describe('MoveQuestions', () => {
  async function opened<T>(ask: (questions: MoveQuestions) => Promise<T>) {
    const answer = ask(TestBed.inject(MoveQuestions));
    const fixture = TestBed.createComponent(Page);
    const sheet =
      await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(MatDialogHarness);
    const host = await sheet.host();
    const element = document.getElementById((await host.getAttribute('id'))!)!;
    return { answer, sheet, element, fixture };
  }

  async function type(element: HTMLElement, text: string, fixture: { detectChanges(): void }) {
    const input = element.querySelector('input')!;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    await Promise.resolve();
  }

  async function press(sheet: MatDialogHarness, text: string): Promise<void> {
    await (await sheet.getHarness(MatButtonHarness.with({ text }))).click();
  }

  afterEach(() => {
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  describe('the estimate', () => {
    it('asks for it in hours for the task, with Renunță and Începe lucrul', async () => {
      const { sheet, element } = await opened((questions) => questions.estimate('KAN-12'));

      expect(element.querySelector('h2')?.textContent.trim()).toBe('Cât estimezi pentru «KAN-12»?');
      expect(element.querySelector('label')?.textContent.trim()).toBe('Estimare (ore)');
      expect(element.querySelector('.kb-form-help')?.textContent.trim()).toBe(
        'Cât timp de lucru crezi că ia. O poți schimba din task.',
      );
      expect(
        await Promise.all(
          (await sheet.getAllHarnesses(MatButtonHarness)).map((button) => button.getText()),
        ),
      ).toEqual(['Renunță', 'Începe lucrul']);
      expect(await (await sheet.host()).getAttribute('aria-labelledby')).toBe('kb-sheet-title');
    });

    it('answers the hours in minutes, with a comma', async () => {
      const { answer, sheet, element, fixture } = await opened((questions) =>
        questions.estimate('KAN-12'),
      );

      await type(element, '1,5', fixture);
      await press(sheet, 'Începe lucrul');

      expect(await answer).toBe(90);
    });

    it('refuses an estimate out of 0.25 to 1000 hours, and answers nothing', async () => {
      const { sheet, element, fixture } = await opened((questions) => questions.estimate('KAN-12'));

      await type(element, '0,2', fixture);
      await press(sheet, 'Începe lucrul');

      expect(element.querySelector('.kb-field-error')?.textContent.trim()).toBe(
        'Între 0,25 și 1000 de ore.',
      );
      expect(document.querySelector('kb-estimate-sheet')).not.toBeNull();
    });

    it('steps a quarter of an hour with the arrows', async () => {
      const { element, fixture } = await opened((questions) => questions.estimate('KAN-12'));
      const input = element.querySelector('input')!;

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(input.value).toBe('0,5');
    });

    it('answers null on Renunță', async () => {
      const { answer, sheet } = await opened((questions) => questions.estimate('KAN-12'));

      await press(sheet, 'Renunță');

      expect(await answer).toBeNull();
    });
  });

  describe('the pause', () => {
    it('asks why, with the characters counted, Renunță and Pune pe pauză', async () => {
      const { sheet, element, fixture } = await opened((questions) =>
        questions.pauseReason('KAN-12'),
      );

      await type(element, 'Aștept clientul', fixture);

      expect(element.querySelector('h2')?.textContent.trim()).toBe('De ce pui pe pauză «KAN-12»?');
      expect(element.querySelector('label')?.textContent.trim()).toBe('Motiv');
      expect(element.querySelector('input')?.getAttribute('maxlength')).toBe('500');
      expect(element.querySelector('.count')?.textContent.trim()).toBe('15/500');
      expect(
        await Promise.all(
          (await sheet.getAllHarnesses(MatButtonHarness)).map((button) => button.getText()),
        ),
      ).toEqual(['Renunță', 'Pune pe pauză']);
    });

    it('answers the reason, trimmed', async () => {
      const { answer, sheet, element, fixture } = await opened((questions) =>
        questions.pauseReason('KAN-12'),
      );

      await type(element, '  Aștept clientul ', fixture);
      await press(sheet, 'Pune pe pauză');

      expect(await answer).toBe('Aștept clientul');
    });

    it('refuses an empty reason', async () => {
      const { sheet, element, fixture } = await opened((questions) =>
        questions.pauseReason('KAN-12'),
      );

      await type(element, '   ', fixture);
      await press(sheet, 'Pune pe pauză');

      expect(element.querySelector('.kb-field-error')?.textContent.trim()).toBe(
        'Scrie motivul pauzei.',
      );
    });

    it('answers null on Renunță', async () => {
      const { answer, sheet } = await opened((questions) => questions.pauseReason('KAN-12'));

      await press(sheet, 'Renunță');

      expect(await answer).toBeNull();
    });
  });
});
