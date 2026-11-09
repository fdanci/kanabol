import { csvFileName, csvOf, saveCsv } from './report-csv';

/**
 * docs/UI_UX_SPEC.md §7.8: "Export CSV" is made in the browser, with commas, quotes and a BOM, so a
 * spreadsheet reads the diacritics, named kanabol-finalizate-2026-11-01-2026-11-30.csv.
 */
describe('report CSV', () => {
  it('starts with a BOM and quotes every field, a row to a line', () => {
    expect(
      csvOf([
        ['Perioada', 'Finalizate'],
        ['2026-11-02', 3],
      ]),
    ).toBe('\uFEFF"Perioada","Finalizate"\r\n"2026-11-02","3"\r\n');
  });

  it('doubles the quotes in a field, and keeps its commas and diacritics', () => {
    expect(csvOf([['Ana, "Pop"', 'Țară']])).toBe('\uFEFF"Ana, ""Pop""","Țară"\r\n');
  });

  it('names the file by what it holds and its days', () => {
    expect(csvFileName('finalizate', '2026-11-01', '2026-11-30')).toBe(
      'kanabol-finalizate-2026-11-01-2026-11-30.csv',
    );
  });

  it('saves it through a link the page clicks, as CSV in UTF-8', () => {
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:report');
    const revoked = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      expect(this.download).toBe('kanabol-finalizate-2026-11-01-2026-11-30.csv');
      expect(this.href).toBe('blob:report');
    });

    saveCsv('\uFEFF"a"\r\n', 'kanabol-finalizate-2026-11-01-2026-11-30.csv');

    const blob = created.mock.calls[0][0] as Blob;
    expect(blob.type).toBe('text/csv;charset=utf-8');
    expect(clicked).toHaveBeenCalledTimes(1);
    expect(revoked).toHaveBeenCalledWith('blob:report');
    created.mockRestore();
    revoked.mockRestore();
    clicked.mockRestore();
  });
});
