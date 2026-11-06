import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p i18n>Salvează</p>
    <button type="button" i18n-aria-label aria-label="Închide"></button>`,
})
class Probe {}

// v1 has only the source language, Romanian, but every text goes through Angular's i18n (docs/SPEC.md §6.4).
describe('i18n', () => {
  it('renders a template text marked for translation in the source language', async () => {
    const fixture = TestBed.createComponent(Probe);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('p')?.textContent).toBe('Salvează');
    expect(element.querySelector('button')?.getAttribute('aria-label')).toBe('Închide');
  });

  it('translates text in code with $localize', () => {
    expect($localize`Anulează`).toBe('Anulează');
  });
});
