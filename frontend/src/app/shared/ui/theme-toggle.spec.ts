import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatSlideToggleHarness } from '@angular/material/slide-toggle/testing';
import { By } from '@angular/platform-browser';
import { ThemeToggle } from './theme-toggle';

@Component({
  imports: [ThemeToggle],
  template: `<kb-theme-toggle [dark]="dark()" (toggled)="toggles = toggles + 1">
    Temă întunecată
  </kb-theme-toggle>`,
})
class Host {
  readonly dark = signal(false);
  toggles = 0;
}

describe('ThemeToggle', () => {
  let fixture: ComponentFixture<Host>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    fixture = TestBed.createComponent(Host);
    loader = TestbedHarnessEnvironment.loader(fixture);
  });

  it('shows the state it is given', async () => {
    const toggle = await loader.getHarness(MatSlideToggleHarness);
    expect(await toggle.isChecked()).toBe(false);

    fixture.componentInstance.dark.set(true);

    expect(await toggle.isChecked()).toBe(true);
  });

  it('shows the label it is given', async () => {
    const toggle = await loader.getHarness(MatSlideToggleHarness);

    expect(await toggle.getLabelText()).toBe('Temă întunecată');
  });

  // docs/UI_UX_SPEC.md §6.0.6: a switch without the check mark, set here rather than in app.config.
  it('shows no icon in its thumb', () => {
    fixture.detectChanges();

    const toggle = fixture.debugElement
      .query(By.directive(MatSlideToggle))
      .injector.get(MatSlideToggle);

    expect(toggle.hideIcon).toBe(true);
  });

  it('tells its parent when it is toggled', async () => {
    const toggle = await loader.getHarness(MatSlideToggleHarness);

    await toggle.toggle();

    expect(fixture.componentInstance.toggles).toBe(1);
  });
});
