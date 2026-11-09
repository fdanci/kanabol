/** Excel reads a CSV as UTF-8, diacritics included, only after this mark. */
const BOM = '\uFEFF';

/**
 * A table as CSV, made in the browser (docs/UI_UX_SPEC.md §7.8): commas between fields, every field in
 * quotes with its own quotes doubled, a row to a line ending in CRLF, and a BOM at the start.
 */
export function csvOf(rows: readonly (readonly (string | number)[])[]): string {
  const line = (row: readonly (string | number)[]) =>
    row.map((field) => `"${String(field).replaceAll('"', '""')}"`).join(',');
  return BOM + rows.map((row) => `${line(row)}\r\n`).join('');
}

/** kanabol-finalizate-2026-11-01-2026-11-30.csv: what it holds, and the days of the interval, both included. */
export function csvFileName(kind: string, from: string, to: string): string {
  return `kanabol-${kind}-${from}-${to}.csv`;
}

/** Saved through a link the page clicks, as the attachments are. */
export function saveCsv(content: string, fileName: string): void {
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}
