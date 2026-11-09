import { Component, input, output } from '@angular/core';
import {
  MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS,
  MatSlideToggle,
  MatSlideToggleDefaultOptions,
} from '@angular/material/slide-toggle';

/**
 * The switch for the theme. Its label comes from the parent, which also keeps the state. It's the only
 * switch, so it sets the switch's defaults itself, and the navbar loads it with @defer: slide-toggle and
 * the forms it brings stay out of the first bundle (docs/SPEC.md §6.2).
 */
@Component({
  imports: [MatSlideToggle],
  selector: 'kb-theme-toggle',
  // A switch without the check mark (docs/UI_UX_SPEC.md §6.0.6).
  providers: [
    {
      provide: MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS,
      useValue: { hideIcon: true } satisfies MatSlideToggleDefaultOptions,
    },
  ],
  template: `<mat-slide-toggle [checked]="dark()" (change)="toggled.emit()"
    ><ng-content
  /></mat-slide-toggle>`,
})
export class ThemeToggle {
  readonly dark = input.required<boolean>();
  readonly toggled = output();
}
