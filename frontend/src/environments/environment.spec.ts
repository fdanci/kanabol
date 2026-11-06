import { environment } from './environment';

// The same build runs in every environment, and nginx or the dev proxy route these (docs/SPEC.md §15).
describe('environment', () => {
  it('has only the two relative base URLs', () => {
    expect(environment).toEqual({ apiBaseUrl: '/api', hubsBaseUrl: '/hubs' });
  });
});
