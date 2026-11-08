import { assigneesChange, historyLine } from './task-history';
import { anEntry } from './task-testing';

// docs/UI_UX_SPEC.md §7.5: «Florin a schimbat Asignați: Ana → Ana, Dan». The entry holds the ids,
// sorted and separated by commas, empty for nobody (docs/SPEC.md §5.1).
describe('assigneesChange', () => {
  const names = new Map([
    ['0193a', 'Dan Ionescu'],
    ['0193b', 'ana Pop'],
    ['0193c', 'Eva Ene'],
  ]);
  const nameOf = (userId: string) => names.get(userId);

  it('names the people before and after, by name whatever the case', () => {
    expect(assigneesChange('0193b', '0193a,0193b', nameOf)).toBe('ana Pop → ana Pop, Dan Ionescu');
  });

  it('says Neasignat for nobody, on either side', () => {
    expect(assigneesChange('', '0193c', nameOf)).toBe('Neasignat → Eva Ene');
    expect(assigneesChange('0193c', '', nameOf)).toBe('Eva Ene → Neasignat');
    expect(assigneesChange(null, null, nameOf)).toBe('Neasignat → Neasignat');
  });

  it('calls someone it no longer knows a former member', () => {
    expect(assigneesChange('0193z', '0193a', nameOf)).toBe('un fost membru → Dan Ionescu');
  });
});

// docs/UI_UX_SPEC.md §7.5, the texts on each field: «Florin a schimbat Prioritate: Medium → High».
describe('historyLine', () => {
  it('writes a title and a priority as old → new, the priority by its label', () => {
    expect(historyLine(anEntry({ oldValue: 'Login SSO', newValue: 'Login cu SSO' }))).toEqual({
      who: 'Florin Danci',
      kind: 'changed',
      field: 'Titlu',
      change: 'Login SSO → Login cu SSO',
    });
    expect(
      historyLine(anEntry({ field: 'Priority', oldValue: 'Medium', newValue: 'High' })),
    ).toEqual(
      expect.objectContaining({ kind: 'changed', field: 'Prioritate', change: 'Medium → High' }),
    );
  });

  it('writes a deadline as its day in ro-RO, and "fără deadline" for none', () => {
    expect(
      historyLine(anEntry({ field: 'DueDate', oldValue: null, newValue: '2026-11-12T12:00:00Z' })),
    ).toEqual(expect.objectContaining({ field: 'Deadline', change: 'fără deadline → 12.11.2026' }));
    expect(
      historyLine(anEntry({ field: 'DueDate', oldValue: '2026-11-12T12:00:00Z', newValue: null })),
    ).toEqual(expect.objectContaining({ change: '12.11.2026 → fără deadline' }));
  });

  it('writes a move as Stare with the names of the columns, "coloană ștearsă" for one gone', () => {
    const entry = anEntry({
      field: 'ColumnId',
      oldValue: 'c9',
      newValue: 'c2',
      names: { c9: null, c2: 'În lucru' },
    });

    expect(historyLine(entry)).toEqual(
      expect.objectContaining({ field: 'Stare', change: 'coloană ștearsă → În lucru' }),
    );
  });

  it('names the assignees from the entry, "Neasignat" for nobody', () => {
    const entry = anEntry({
      field: 'Assignees',
      oldValue: '',
      newValue: 'u1,u2',
      names: { u1: 'Dan Pop', u2: 'Ana Ionescu' },
    });

    expect(historyLine(entry)).toEqual(
      expect.objectContaining({ field: 'Asignați', change: 'Neasignat → Ana Ionescu, Dan Pop' }),
    );
  });

  it('says only that the description changed, without its texts', () => {
    const line = historyLine(
      anEntry({ field: 'Description', oldValue: 'Vechea **descriere**', newValue: 'Noua' }),
    );

    expect(line.kind).toBe('description');
    expect(JSON.stringify(line)).not.toContain('descriere**');
    expect(JSON.stringify(line)).not.toContain('Noua');
  });

  it('tells an archiving from a restoring', () => {
    expect(
      historyLine(anEntry({ field: 'ArchivedAt', newValue: '2026-11-08T10:00:00Z' })).kind,
    ).toBe('archived');
    expect(
      historyLine(anEntry({ field: 'ArchivedAt', oldValue: '2026-11-08T10:00:00Z' })).kind,
    ).toBe('restored');
  });

  it('writes a field it does not know by its name, with its values as they are', () => {
    expect(historyLine(anEntry({ field: 'Estimate', oldValue: '', newValue: '3' }))).toEqual(
      expect.objectContaining({ kind: 'changed', field: 'Estimate', change: '— → 3' }),
    );
  });
});
