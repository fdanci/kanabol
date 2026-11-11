/**
 * What a board column means (docs/SPEC.md §4.4, "Coloanele fixe"): the four fixed kinds every project has
 * once, in this order, and the project's own columns, Custom, between Backlog and Done. A task in the Done
 * column, Gata, is finished.
 */
export type ColumnKind = 'Backlog' | 'InProgress' | 'Paused' | 'Done' | 'Custom';
