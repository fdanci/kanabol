import { TestBed } from '@angular/core/testing';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeRo from '@angular/common/locales/ro';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { PaginatorLabels, providePaginatorLabels } from './paginator-labels';

// docs/UI_UX_SPEC.md §6.4 and §11.
describe('PaginatorLabels', () => {
  beforeEach(() => {
    registerLocaleData(localeRo, 'ro-RO');
    TestBed.configureTestingModule({
      providers: [{ provide: LOCALE_ID, useValue: 'ro-RO' }, providePaginatorLabels()],
    });
  });

  it('has the paginators speak Romanian, with Romanian numbers', () => {
    const labels = TestBed.inject(MatPaginatorIntl);

    expect(labels).toBeInstanceOf(PaginatorLabels);
    expect(labels.itemsPerPageLabel).toBe('Pe pagină:');
    expect(labels.nextPageLabel).toBe('Pagina următoare');
    expect(labels.getRangeLabel(0, 25, 60)).toBe('1 – 25 din 60');
    expect(labels.getRangeLabel(40, 25, 1012)).toBe('1.001 – 1.012 din 1.012');
    expect(labels.getRangeLabel(0, 25, 0)).toBe('0 din 0');
  });
});
