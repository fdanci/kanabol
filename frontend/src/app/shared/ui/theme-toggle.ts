import { Component, input, output } from '@angular/core';
import { MatSlideToggle } from '@angular/material/slide-toggle';

/** The switch for the theme. Its label comes from the parent, which also keeps the state. */
@Component({
  imports: [MatSlideToggle],
  selector: 'kb-theme-toggle',
  template: `<mat-slide-toggle [checked]="dark()" (change)="toggled.emit()"
    ><ng-content
  /></mat-slide-toggle>`,
})
export class ThemeToggle {
  readonly dark = input.required<boolean>();
  readonly toggled = output();
}
