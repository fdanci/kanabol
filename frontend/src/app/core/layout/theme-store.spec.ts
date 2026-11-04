import { ThemeStore } from './theme-store';

interface FakeMediaQuery {
  matches: boolean;
  listener?: (event: { matches: boolean }) => void;
  addEventListener: (type: string, listener: (event: { matches: boolean }) => void) => void;
}

function systemPrefersDark(matches: boolean): FakeMediaQuery {
  const query: FakeMediaQuery = {
    matches,
    addEventListener: (_type, listener) => {
      query.listener = listener;
    },
  };
  vi.stubGlobal('matchMedia', () => query);
  return query;
}

// A store is a plain class with signals, so it needs no TestBed.
describe('ThemeStore', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    delete root.dataset['theme'];
    systemPrefersDark(false);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('follows the system while nothing is chosen', () => {
    systemPrefersDark(true);

    const store = new ThemeStore();

    expect(store.isDark()).toBe(true);
    expect(root.dataset['theme']).toBeUndefined();
    expect(localStorage.getItem('kb.theme')).toBeNull();
  });

  it('follows a change in the system while nothing is chosen', () => {
    const system = systemPrefersDark(false);
    const store = new ThemeStore();

    system.listener?.({ matches: true });

    expect(store.isDark()).toBe(true);
  });

  it('sets data-theme and remembers the choice when toggled', () => {
    const store = new ThemeStore();

    store.toggle();

    expect(store.isDark()).toBe(true);
    expect(root.dataset['theme']).toBe('dark');
    expect(localStorage.getItem('kb.theme')).toBe('dark');
  });

  it('switches back when toggled again', () => {
    const store = new ThemeStore();

    store.toggle();
    store.toggle();

    expect(store.isDark()).toBe(false);
    expect(root.dataset['theme']).toBe('light');
    expect(localStorage.getItem('kb.theme')).toBe('light');
  });

  it('restores the saved choice, whatever the system says', () => {
    localStorage.setItem('kb.theme', 'dark');
    systemPrefersDark(false);

    const store = new ThemeStore();

    expect(store.isDark()).toBe(true);
    expect(root.dataset['theme']).toBe('dark');
  });

  it('stops following the system once something is chosen', () => {
    const system = systemPrefersDark(false);
    const store = new ThemeStore();
    store.toggle();
    store.toggle();

    system.listener?.({ matches: true });

    expect(store.isDark()).toBe(false);
  });

  it('ignores a saved value it does not know', () => {
    localStorage.setItem('kb.theme', 'sepia');

    const store = new ThemeStore();

    expect(store.isDark()).toBe(false);
    expect(root.dataset['theme']).toBeUndefined();
  });
});
