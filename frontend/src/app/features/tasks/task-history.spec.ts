import { assigneesChange } from './task-history';

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
