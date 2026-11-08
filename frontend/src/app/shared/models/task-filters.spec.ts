import { convertToParamMap } from '@angular/router';
import {
  NO_TASK_FILTERS,
  filtersFromQuery,
  filtersToQuery,
  hasFilters,
  sortFromQuery,
  sortToQuery,
} from './task-filters';

describe('task filters in the address', () => {
  it('reads nothing as no filter', () => {
    expect(filtersFromQuery(convertToParamMap({}))).toEqual(NO_TASK_FILTERS);
    expect(hasFilters(NO_TASK_FILTERS)).toBe(false);
  });

  it('reads every filter back from what it wrote', () => {
    const filters = {
      assignee: 'me',
      priorities: ['High' as const, 'Critical' as const],
      overdue: true,
      search: 'login',
      archived: true,
    };

    const query = filtersToQuery(filters);

    expect(query).toEqual({
      assignee: 'me',
      priority: 'High,Critical',
      overdue: '1',
      q: 'login',
      archived: '1',
    });
    expect(filtersFromQuery(convertToParamMap(query))).toEqual(filters);
    expect(hasFilters(filters)).toBe(true);
  });

  it('writes null for the filters that are off, so the address loses them', () => {
    expect(filtersToQuery(NO_TASK_FILTERS)).toEqual({
      assignee: null,
      priority: null,
      overdue: null,
      q: null,
      archived: null,
    });
  });

  it('leaves out what is not a priority, and keeps the order of the four', () => {
    expect(
      filtersFromQuery(convertToParamMap({ priority: 'Critical,Urgent,low,Low' })).priorities,
    ).toEqual(['Low', 'Critical']);
  });

  it('does not count archived tasks alone as a filter of the list', () => {
    expect(hasFilters({ ...NO_TASK_FILTERS, archived: true })).toBe(false);
  });

  it('reads the sort, by key from the newest unless it says otherwise', () => {
    expect(sortFromQuery(convertToParamMap({}))).toEqual({ field: 'key', descending: true });
    expect(sortFromQuery(convertToParamMap({ sort: 'title:asc' }))).toEqual({
      field: 'title',
      descending: false,
    });
    expect(sortFromQuery(convertToParamMap({ sort: 'color:asc' }))).toEqual({
      field: 'key',
      descending: true,
    });
    expect(sortToQuery({ field: 'dueDate', descending: true })).toBe('dueDate:desc');
    expect(sortToQuery({ field: 'key', descending: true })).toBeNull();
  });

  // "Task-urile mele" go by deadline from the soonest (docs/UI_UX_SPEC.md §7.2).
  it('reads and writes the sort against another default when given one', () => {
    const byDeadline = { field: 'dueDate', descending: false } as const;

    expect(sortFromQuery(convertToParamMap({}), byDeadline)).toEqual(byDeadline);
    expect(sortFromQuery(convertToParamMap({ sort: 'nope' }), byDeadline)).toEqual(byDeadline);
    expect(sortToQuery(byDeadline, byDeadline)).toBeNull();
    expect(sortToQuery({ field: 'key', descending: true }, byDeadline)).toBe('key:desc');
  });
});
