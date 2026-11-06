import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '../../shared/ui/empty-state';

/** A view of the project whose group in Tasks.md hasn't been built yet: the board, the list, the reports. */
@Component({
  selector: 'kb-project-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState],
  template: `
    <kb-empty-state icon="construction" i18n-title title="Pagina e în lucru">
      <p i18n>Apare aici într-o versiune viitoare.</p>
    </kb-empty-state>
  `,
})
export class ProjectPlaceholder {}
