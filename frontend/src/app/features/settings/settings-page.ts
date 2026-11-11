import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { Snackbar } from '../../shared/ui/snackbar';
import { SettingsApi, WorkScheduleDto } from './settings-api';

/** The days of the week, ISO, as the chips name them (docs/UI_UX_SPEC.md §7.9). */
const DAYS = [
  { day: 1, short: $localize`L`, name: $localize`Luni` },
  { day: 2, short: $localize`Ma`, name: $localize`Marți` },
  { day: 3, short: $localize`Mi`, name: $localize`Miercuri` },
  { day: 4, short: $localize`J`, name: $localize`Joi` },
  { day: 5, short: $localize`V`, name: $localize`Vineri` },
  { day: 6, short: $localize`S`, name: $localize`Sâmbătă` },
  { day: 7, short: $localize`D`, name: $localize`Duminică` },
];

type SettingsState = 'loading' | 'ready' | 'error';

/**
 * "Setări generale" (docs/UI_UX_SPEC.md §7.9), for settings.manage: the card "Program de lucru", its work
 * days as seven chips, its hours "De la" and "Până la", the time zone of the server's configuration to read,
 * and "Salvează", on only once something changed. Saved, it says so; changed meanwhile by someone else (409
 * Settings.ChangedMeanwhile, which the interceptor says), it reads the schedule again.
 */
@Component({
  selector: 'kb-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, MatButton, MatTooltip, PageHeader, Skeleton],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss',
})
export class SettingsPage {
  private readonly api = inject(SettingsApi);
  private readonly snackbar = inject(Snackbar);

  protected readonly heading = $localize`Setări generale`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Setări generale` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca setările.`;
  protected readonly days = DAYS;

  protected readonly state = signal<SettingsState>('loading');
  protected readonly saving = signal(false);
  private readonly saved = signal<WorkScheduleDto | null>(null);
  protected readonly chosenDays = signal<number[]>([]);
  protected readonly start = signal('');
  protected readonly end = signal('');
  protected readonly timeZone = computed(() => this.saved()?.timeZone ?? '');

  protected readonly daysError = computed(() =>
    this.chosenDays().length === 0 ? $localize`Alege cel puțin o zi.` : null,
  );
  protected readonly hoursError = computed(() =>
    this.start() === '' || this.end() === '' || this.start() >= this.end()
      ? $localize`Ora de început trebuie să fie înaintea celei de sfârșit.`
      : null,
  );
  protected readonly changed = computed(() => {
    const saved = this.saved();
    return (
      saved !== null &&
      (this.start() !== saved.start ||
        this.end() !== saved.end ||
        this.chosenDays().join(',') !== [...saved.days].sort((a, b) => a - b).join(','))
    );
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    if (this.saved() === null) {
      this.state.set('loading');
    }
    try {
      this.show(await firstValueFrom(this.api.workSchedule()));
      this.state.set('ready');
    } catch {
      if (this.saved() === null) {
        this.state.set('error');
      }
    }
  }

  protected isChosen(day: number): boolean {
    return this.chosenDays().includes(day);
  }

  protected toggle(day: number): void {
    this.chosenDays.update((days) =>
      days.includes(day)
        ? days.filter((found) => found !== day)
        : [...days, day].sort((a, b) => a - b),
    );
  }

  protected async save(): Promise<void> {
    if (!this.changed() || this.saving() || this.daysError() || this.hoursError()) {
      return;
    }
    this.saving.set(true);
    try {
      this.show(
        await firstValueFrom(
          this.api.saveWorkSchedule({
            days: this.chosenDays(),
            start: this.start(),
            end: this.end(),
          }),
        ),
      );
      this.snackbar.show($localize`Programul de lucru a fost salvat.`);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.load();
      }
    } finally {
      this.saving.set(false);
    }
  }

  private show(schedule: WorkScheduleDto): void {
    this.saved.set(schedule);
    this.chosenDays.set([...schedule.days].sort((a, b) => a - b));
    this.start.set(schedule.start);
    this.end.set(schedule.end);
  }
}
