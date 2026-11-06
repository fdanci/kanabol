import { formatNumber } from '@angular/common';
import { Injectable, LOCALE_ID, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** What mat-paginator says, in Romanian (docs/UI_UX_SPEC.md §6.4, §11): "1 – 25 din 60". */
@Injectable()
export class PaginatorLabels extends MatPaginatorIntl {
  private readonly locale = inject(LOCALE_ID);

  override itemsPerPageLabel = $localize`Pe pagină:`;
  override nextPageLabel = $localize`Pagina următoare`;
  override previousPageLabel = $localize`Pagina anterioară`;
  override firstPageLabel = $localize`Prima pagină`;
  override lastPageLabel = $localize`Ultima pagină`;

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return $localize`0 din 0`;
    }
    const first = page * pageSize + 1;
    const last = Math.min(length, (page + 1) * pageSize);
    return $localize`${this.number(first)}:first: – ${this.number(last)}:last: din ${this.number(length)}:total:`;
  };

  private number(value: number): string {
    return formatNumber(value, this.locale, '1.0-0');
  }
}
