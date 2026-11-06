/** One page of a list, as the API sends it (docs/SPEC.md §7.3): `?page=1&pageSize=50`. */
export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}
