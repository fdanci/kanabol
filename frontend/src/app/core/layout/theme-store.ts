import { Injectable, computed, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'kb.theme';

function savedTheme(): Theme | null {
  const saved = localStorage.getItem(STORAGE_KEY);
  return saved === 'light' || saved === 'dark' ? saved : null;
}

/**
 * The theme the user chose. Until they choose, the page follows the system through
 * prefers-color-scheme, and data-theme stays unset (docs/UI_UX_SPEC.md §2.3).
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly chosen = signal(savedTheme());
  private readonly systemIsDark = signal(false);

  readonly isDark = computed(() => {
    const chosen = this.chosen();
    return chosen ? chosen === 'dark' : this.systemIsDark();
  });

  constructor() {
    const system = window.matchMedia('(prefers-color-scheme: dark)');
    this.systemIsDark.set(system.matches);
    system.addEventListener('change', (event) => {
      this.systemIsDark.set(event.matches);
    });

    const chosen = this.chosen();
    if (chosen) {
      document.documentElement.dataset['theme'] = chosen;
    }
  }

  toggle(): void {
    const theme: Theme = this.isDark() ? 'light' : 'dark';
    this.chosen.set(theme);
    document.documentElement.dataset['theme'] = theme;
    localStorage.setItem(STORAGE_KEY, theme);
  }
}
