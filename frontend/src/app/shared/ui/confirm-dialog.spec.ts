import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialogHarness } from '@angular/material/dialog/testing';
import { Confirmation, ConfirmationRequest } from './confirm-dialog';

const ARCHIVE: ConfirmationRequest = {
  title: 'Arhivezi task-ul «Login SSO»?',
  message: 'Nu mai apare pe board, dar rămâne în istoric.',
  confirmLabel: 'Arhivează',
  icon: 'archive',
  destructive: true,
};

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Page {}

describe('Confirmation', () => {
  function open(request: ConfirmationRequest) {
    const answer = TestBed.inject(Confirmation).ask(request);
    const loader = TestbedHarnessEnvironment.documentRootLoader(TestBed.createComponent(Page));
    return { answer, loader };
  }

  async function dialog() {
    const { answer, loader } = open(ARCHIVE);
    const sheet = await loader.getHarness(MatDialogHarness);
    return { answer, sheet };
  }

  it('says what happens and what follows from it, and names itself by them', async () => {
    const { sheet } = await dialog();
    const host = await sheet.host();

    const title = document.getElementById((await host.getAttribute('aria-labelledby'))!);
    const message = document.getElementById((await host.getAttribute('aria-describedby'))!);
    expect(title?.tagName).toBe('H2');
    expect(title?.textContent.trim()).toBe('Arhivezi task-ul «Login SSO»?');
    expect(message?.textContent.trim()).toBe('Nu mai apare pe board, dar rămâne în istoric.');
    expect(await host.getAttribute('role')).toBe('alertdialog');
  });

  it('offers Anulează and the verb, in that order', async () => {
    const { sheet } = await dialog();

    const buttons = await sheet.getAllHarnesses(MatButtonHarness);

    expect(await Promise.all(buttons.map((button) => button.getText()))).toEqual([
      'Anulează',
      'Arhivează',
    ]);
  });

  it('answers yes with the verb', async () => {
    const { answer, sheet } = await dialog();

    await (await sheet.getHarness(MatButtonHarness.with({ text: 'Arhivează' }))).click();

    expect(await answer).toBe(true);
  });

  it('answers no with Anulează, and with Escape', async () => {
    const first = await dialog();
    await (await first.sheet.getHarness(MatButtonHarness.with({ text: 'Anulează' }))).click();
    expect(await first.answer).toBe(false);

    const second = await dialog();
    await second.sheet.close();
    expect(await second.answer).toBe(false);
  });

  it('makes a destructive verb red, with the focus away from it', async () => {
    const { sheet } = await dialog();

    const verb = await (
      await sheet.getHarness(MatButtonHarness.with({ text: 'Arhivează' }))
    ).host();
    const cancel = await (
      await sheet.getHarness(MatButtonHarness.with({ text: 'Anulează' }))
    ).host();

    expect(await verb.hasClass('kb-danger')).toBe(true);
    expect(await cancel.getAttribute('cdkfocusinitial')).toBe('');
    expect(await verb.getAttribute('cdkfocusinitial')).toBeNull();
  });

  it('puts the focus on the verb when nothing is lost', async () => {
    const { loader } = open({ ...ARCHIVE, destructive: false, title: 'Trimiți invitația?' });
    const sheet = await loader.getHarness(MatDialogHarness);

    const verb = await (
      await sheet.getHarness(MatButtonHarness.with({ text: 'Arhivează' }))
    ).host();

    expect(await verb.hasClass('kb-default')).toBe(true);
    expect(await verb.getAttribute('cdkfocusinitial')).toBe('');
  });

  it('asks the same thing before any unsaved change is thrown away', async () => {
    const confirmation = TestBed.inject(Confirmation);
    const ask = vi.spyOn(confirmation, 'ask').mockResolvedValue(true);

    expect(await confirmation.discardChanges()).toBe(true);
    expect(ask).toHaveBeenCalledWith({
      title: 'Renunți la modificări?',
      message: 'Ce ai schimbat nu a fost salvat și se pierde.',
      confirmLabel: 'Renunță',
      icon: 'undo',
      destructive: true,
    });
  });
});
