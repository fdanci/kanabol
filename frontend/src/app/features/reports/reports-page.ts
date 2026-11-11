import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MAT_DATE_LOCALE, MatOption, provideNativeDateAdapter } from '@angular/material/core';
import {
  MatDatepicker,
  MatDatepickerInput,
  MatDatepickerToggle,
} from '@angular/material/datepicker';
import { MatSelect } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { map } from 'rxjs';
import { Chart } from '../../shared/ui/chart';
import type { ChartSeries } from '../../shared/ui/chart';
import { EmptyState } from '../../shared/ui/empty-state';
import { Crumb, PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatTile } from '../../shared/ui/stat-tile';
import { formatDuration, formatWorkDuration } from './durations';
import { openTaskPanel } from '../../shared/task-panel';
import {
  CompletedReportDto,
  CycleTimeReportDto,
  DeadlinesReportDto,
  ReportWorkScheduleDto,
  ReportWorkTimeRow,
  WorkTimeReportDto,
} from './models';
import { csvFileName, csvOf, saveCsv } from './report-csv';
import {
  ReportFilters,
  ReportGranularity,
  ReportPreset,
  dateOf,
  dayOf,
  earliestFrom,
  filtersFromQuery,
  filtersToQuery,
  latestTo,
  presetOf,
  presetRange,
} from './report-filters';
import { ReportsStore } from './reports-store';

/** "Toate proiectele" and "Oricine": mat-select takes null for no choice at all and shows nothing. */
const EVERY = '';

const PERCENT = new Intl.NumberFormat('ro-RO', { style: 'percent', maximumFractionDigits: 0 });
const WEEK_LABEL = new Intl.DateTimeFormat('ro-RO', { day: 'numeric', month: 'short' });
const WEEK_ROW = new Intl.DateTimeFormat('ro-RO', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
});
const MONTH_LABEL = new Intl.DateTimeFormat('ro-RO', { month: 'short', year: 'numeric' });
const MONTH_ROW = new Intl.DateTimeFormat('ro-RO', { month: 'long', year: 'numeric' });
const HOURS = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 0 });

interface PeriodRow {
  /** The day the period starts, as YYYY-MM-DD, for the CSV. */
  day: string;
  label: string;
  row: string;
}

interface DurationRow {
  name: string;
  averageSeconds: number | null;
  medianSeconds: number | null;
}

/**
 * /reports (docs/UI_UX_SPEC.md §7.8): the filters' card, every one in the address, then "Task-uri
 * finalizate", "Lead time și cycle time" and "Deadline-uri", each read on its own with its tiles, its chart
 * (kb-chart, loaded with @defer, so chart.js stays out of the first bundle), the table of its data with
 * "Export CSV", its skeleton, its empty text and its error.
 * The project's "Rapoarte" tab (§7.3) is the same page, which projects.routes.ts loads with
 * `projectFixed` in its data: the project is the route's, without a title, a breadcrumb or a project
 * select, and stays out of the address.
 */
@Component({
  selector: 'kb-reports-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    ReportsStore,
    provideNativeDateAdapter(),
    { provide: MAT_DATE_LOCALE, useValue: 'ro-RO' },
  ],
  imports: [
    MatButton,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    MatOption,
    MatSelect,
    MatTableModule,
    Chart,
    EmptyState,
    PageHeader,
    Skeleton,
    StatTile,
  ],
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
})
export class ReportsPage {
  protected readonly store = inject(ReportsStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly today = new Date();
  protected readonly projectFixed = this.route.snapshot.data['projectFixed'] === true;
  private readonly projectRoute = this.route.parent ?? this.route;
  private readonly routeProject = toSignal(
    this.projectRoute.paramMap.pipe(map((params) => params.get('id'))),
    {
      initialValue: this.projectRoute.snapshot.paramMap.get('id'),
    },
  );

  protected readonly heading = $localize`Rapoarte`;
  protected readonly crumbs: Crumb[] = [
    { label: $localize`Acasă`, link: '/' },
    { label: $localize`Rapoarte` },
  ];
  protected readonly loadFailed = $localize`Nu am putut încărca raportul.`;
  protected readonly noneFinished = $localize`Niciun task finalizat în interval.`;
  protected readonly noneWithDeadline = $localize`Niciun task cu deadline în interval.`;
  protected readonly noneEstimated = $localize`Niciun task finalizat cu estimare în acest interval.`;
  protected readonly workTimeColumns = ['name', 'count', 'estimated', 'worked', 'deviation'];
  protected readonly overColumns = ['task', 'estimated', 'worked', 'overrun'];
  protected readonly every = EVERY;
  protected readonly presets: { value: ReportPreset; label: string }[] = [
    { value: 'week', label: $localize`Săptămâna asta` },
    { value: 'month', label: $localize`Luna asta` },
    { value: 'last30', label: $localize`Ultimele 30 de zile` },
  ];
  protected readonly granularities: { value: ReportGranularity; label: string }[] = [
    { value: 'week', label: $localize`Săptămână` },
    { value: 'month', label: $localize`Lună` },
  ];
  protected readonly periodColumns = ['period', 'count'];
  protected readonly peopleColumns = ['name', 'count'];
  protected readonly durationColumns = ['name', 'average', 'median'];
  protected readonly deadlineColumns = ['period', 'onTime', 'late'];

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: convertToParamMap({}),
  });
  private readonly read = computed(() =>
    filtersFromQuery(this.params(), this.today, this.projectFixed ? this.routeProject() : null),
  );
  protected readonly filters = computed(() => this.read().filters);
  protected readonly activePreset = computed(() => presetOf(this.filters(), this.today));
  protected readonly fromDate = computed(() => dateOf(this.filters().from));
  protected readonly toDate = computed(() => dateOf(this.filters().to));
  protected readonly earliestFromDate = computed(() => dateOf(earliestFrom(this.filters().to)));
  protected readonly latestToDate = computed(() => dateOf(latestTo(this.filters().from)));

  protected readonly people = computed(() => {
    const people = this.store.people();
    const user = this.filters().user;
    if (user === null || people.some((person) => person.userId === user)) {
      return people;
    }
    return [
      ...people,
      { userId: user, displayName: this.store.nameOf(user) ?? $localize`Utilizator ales` },
    ];
  });

  protected readonly completedPeriods = computed(() =>
    this.periods(this.store.completed.value()?.series ?? []),
  );
  protected readonly deadlinePeriods = computed(() =>
    this.periods(this.store.deadlines.value()?.series ?? []),
  );
  protected readonly completedLabels = computed(() =>
    this.completedPeriods().map((period) => period.label),
  );
  protected readonly deadlineLabels = computed(() =>
    this.deadlinePeriods().map((period) => period.label),
  );
  protected readonly perPerson = computed(() => {
    const report = this.store.completed.value();
    return report ? report.byUser.reduce((sum, person) => sum + person.count, 0) : null;
  });

  /** With a project, each of its columns; without one, lead and cycle time side by side. */
  protected readonly durationRows = computed<DurationRow[]>(() => {
    const report = this.store.cycleTime.value();
    if (!report) {
      return [];
    }
    return (
      report.perColumn ?? [
        { name: $localize`Lead time`, ...report.leadTime },
        { name: $localize`Cycle time`, ...report.cycleTime },
      ]
    );
  });

  protected readonly completedChart = computed<readonly ChartSeries[]>(() => [
    {
      label: $localize`Finalizate`,
      values: this.store.completed.value()?.series.map((period) => period.count) ?? [],
      tone: 'primary',
    },
  ]);
  protected readonly durationChart = computed<readonly ChartSeries[]>(() => [
    {
      label: $localize`Media`,
      values: this.durationRows().map((row) => hours(row.averageSeconds)),
      tone: 'primary',
    },
    {
      label: $localize`Mediana`,
      values: this.durationRows().map((row) => hours(row.medianSeconds)),
      tone: 'secondary',
    },
  ]);
  protected readonly deadlineChart = computed<readonly ChartSeries[]>(() => {
    const series = this.store.deadlines.value()?.series ?? [];
    return [
      { label: $localize`La timp`, values: series.map((period) => period.onTime), tone: 'primary' },
      {
        label: $localize`După termen`,
        values: series.map((period) => period.late),
        tone: 'secondary',
      },
    ];
  });
  /** Estimated and worked of each person, in hours on the axis, the full duration in the tooltip. */
  protected readonly workTimeChart = computed<readonly ChartSeries[]>(() => {
    const people = this.store.workTime.value()?.byUser ?? [];
    return [
      {
        label: $localize`Estimat`,
        values: people.map((person) => hours(person.estimatedSeconds)),
        tone: 'primary',
      },
      {
        label: $localize`Lucrat`,
        values: people.map((person) => hours(person.workedSeconds)),
        tone: 'secondary',
      },
    ];
  });
  protected readonly workTimeLabels = computed(
    () => this.store.workTime.value()?.byUser.map((person) => person.name) ?? [],
  );
  /** "(luni–vineri, 08:30–16:30)", from the schedule of now. */
  protected readonly scheduleNote = computed(() => {
    const schedule = this.store.schedule();
    return schedule
      ? $localize`Timpul lucrat se numără doar în programul de lucru (${scheduleText(schedule)}:schedule:).`
      : $localize`Timpul lucrat se numără doar în programul de lucru.`;
  });
  protected readonly durationLabels = computed(() => this.durationRows().map((row) => row.name));
  protected readonly formatHours = (value: number) => formatDuration(value * 3600);
  /** The work-time chart's tooltip, in hours and minutes, never days (docs/UI_UX_SPEC.md §7.8, group 23). */
  protected readonly formatWorkHours = (value: number) => formatWorkDuration(value * 3600);
  protected readonly formatHourTick = (value: number) => $localize`${HOURS.format(value)} h`;

  constructor() {
    if (!this.projectFixed) {
      void this.store.loadProjects();
    }
    void this.store.loadSchedule();

    // Only the address starts a load. A wrong interval goes back to the last 30 days, in the address too.
    effect(() => {
      const read = this.read();
      untracked(() => {
        if (read.valid) {
          this.store.load(read.filters);
        } else {
          void this.navigate({ from: null, to: null }, true);
        }
      });
    });
  }

  protected choosePreset(preset: ReportPreset): void {
    this.change(presetRange(preset, this.today));
  }

  protected chooseFrom(date: Date | null): void {
    if (date) {
      this.change({ from: dayOf(date) });
    }
  }

  protected chooseTo(date: Date | null): void {
    if (date) {
      this.change({ to: dayOf(date) });
    }
  }

  /** Another project has other people: the person chosen goes. */
  protected chooseProject(project: string): void {
    this.change({ project: project || null, user: null });
  }

  protected chooseUser(user: string): void {
    this.change({ user: user || null });
  }

  protected chooseGranularity(granularity: ReportGranularity): void {
    this.change({ granularity });
  }

  protected archived(archivedAt: string | null): boolean {
    return archivedAt !== null;
  }

  protected duration(seconds: number | null): string {
    return formatDuration(seconds);
  }

  /** Estimare și timp lucrat counts hours of the schedule: hours and minutes only (group 23). */
  protected workDuration(seconds: number | null): string {
    return formatWorkDuration(seconds);
  }

  protected rate(report: DeadlinesReportDto | null): string | null {
    if (!report) {
      return null;
    }
    return report.onTimeRate === null ? '–' : PERCENT.format(report.onTimeRate);
  }

  protected median(seconds: number | null): string {
    return $localize`mediana ${formatDuration(seconds)}`;
  }

  protected exportCompleted(report: CompletedReportDto): void {
    const rows = this.completedPeriods().map((period, index) => [
      period.day,
      report.series[index].count,
    ]);
    this.save('finalizate', [[$localize`Perioada`, $localize`Finalizate`], ...rows]);
  }

  protected exportDurations(report: CycleTimeReportDto): void {
    const first = report.perColumn ? $localize`Coloana` : $localize`Măsura`;
    const rows = this.durationRows().map((row) => [
      row.name,
      row.averageSeconds ?? '',
      row.medianSeconds ?? '',
    ]);
    this.save('durate', [
      [first, $localize`Media (secunde)`, $localize`Mediana (secunde)`],
      ...rows,
    ]);
  }

  /** "+18 %" or "−5 %" of the worked time against the estimate, "—" without one. */
  protected deviation(row: ReportWorkTimeRow | WorkTimeReportDto): string {
    const value =
      'deviation' in row
        ? row.deviation
        : row.estimatedSeconds > 0
          ? (row.workedSeconds - row.estimatedSeconds) / row.estimatedSeconds
          : null;
    return deviationText(value);
  }

  protected openTask(key: string): void {
    void openTaskPanel(this.router, key);
  }

  protected exportWorkTime(report: WorkTimeReportDto): void {
    const row = (kind: string, name: string, values: ReportWorkTimeRow) => [
      kind,
      name,
      values.count,
      values.estimatedSeconds,
      values.workedSeconds,
      this.deviation(values),
    ];
    this.save('estimare', [
      [
        $localize`Tabel`,
        $localize`Nume`,
        $localize`Task-uri`,
        $localize`Estimat (secunde)`,
        $localize`Lucrat (secunde)`,
        $localize`Abatere`,
      ],
      ...report.byUser.map((person) => row($localize`Pe om`, person.name, person)),
      ...report.byProject.map((project) =>
        row($localize`Pe proiect`, `${project.name} (${project.key})`, project),
      ),
    ]);
  }

  protected exportDeadlines(report: DeadlinesReportDto): void {
    const rows = this.deadlinePeriods().map((period, index) => [
      period.day,
      report.series[index].onTime,
      report.series[index].late,
    ]);
    this.save('deadline-uri', [
      [$localize`Perioada`, $localize`La timp`, $localize`După termen`],
      ...rows,
    ]);
  }

  private save(kind: string, rows: (string | number)[][]): void {
    const { from, to } = this.filters();
    saveCsv(csvOf(rows), csvFileName(kind, from, to));
  }

  /**
   * A period by the day it starts on where the person is, which is the day in periodStart: the API steps the
   * periods on the clock the interval came with.
   */
  private periods(series: readonly { periodStart: string }[]): PeriodRow[] {
    const month = this.filters().granularity === 'month';
    return series.map(({ periodStart }) => {
      const day = periodStart.slice(0, 10);
      const date = dateOf(day);
      return {
        day,
        label: date ? (month ? MONTH_LABEL : WEEK_LABEL).format(date) : day,
        row: date ? (month ? MONTH_ROW : WEEK_ROW).format(date) : day,
      };
    });
  }

  private change(change: Partial<ReportFilters>): void {
    void this.navigate(
      filtersToQuery({ ...this.filters(), ...change }, this.today, this.projectFixed),
      false,
    );
  }

  private navigate(
    queryParams: Record<string, string | null>,
    replaceUrl: boolean,
  ): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }
}

const DAY_NAMES = [
  $localize`luni`,
  $localize`marți`,
  $localize`miercuri`,
  $localize`joi`,
  $localize`vineri`,
  $localize`sâmbătă`,
  $localize`duminică`,
];

/** "luni–vineri, 08:30–16:30", or the days one by one when they don't follow each other. */
function scheduleText(schedule: ReportWorkScheduleDto): string {
  const days = [...schedule.days].sort((a, b) => a - b);
  const following =
    days.length > 2 && days.every((day, index) => index === 0 || day === days[index - 1] + 1);
  const named = following
    ? `${DAY_NAMES[days[0] - 1]}–${DAY_NAMES[days[days.length - 1] - 1]}`
    : days.map((day) => DAY_NAMES[day - 1]).join(', ');
  return `${named}, ${schedule.start}–${schedule.end}`;
}

/** A share with its sign, the minus a true one: "+18 %", "−5 %", "0 %", "—" for none. */
function deviationText(value: number | null): string {
  if (value === null) {
    return '—';
  }
  const percent = Math.round(value * 100);
  const sign = percent > 0 ? '+' : percent < 0 ? '−' : '';
  return `${sign}${String(Math.abs(percent))} %`;
}

/** The chart's axis is in hours; its tooltip writes the duration in full. */
function hours(seconds: number | null): number | null {
  return seconds === null ? null : seconds / 3600;
}
