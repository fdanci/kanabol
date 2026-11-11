import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDatepickerInput } from '@angular/material/datepicker';
import {
  MatCalendarHarness,
  MatDatepickerInputHarness,
  MatDatepickerToggleHarness,
} from '@angular/material/datepicker/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { ChartConfiguration } from 'chart.js';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { CHART_RENDERER, ChartHandle } from '../../shared/ui/chart';
import {
  CompletedReportDto,
  CycleTimeReportDto,
  DeadlinesReportDto,
  ReportPersonDto,
  ReportProjectDto,
  ReportWorkScheduleDto,
  WorkTimeReportDto,
} from './models';
import { ReportFilters, dayOf, presetRange } from './report-filters';
import { ReportsApi } from './reports-api';
import { ReportsPage } from './reports-page';
import {
  aCompletedReport,
  aCycleTimeReport,
  aDeadlinesReport,
  aWorkTimeReport,
} from './reports-testing';

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class Elsewhere {}

/** The project's page, as far as its tabs go: an outlet under /projects/:id. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
class ProjectLike {}

type Read<T> = ReturnType<typeof vi.fn<(filters: ReportFilters) => Observable<T>>>;

/**
 * docs/UI_UX_SPEC.md §7.8: the filters' card, every filter in the address; "Task-uri finalizate", "Lead time
 * și cycle time" and "Deadline-uri", each read on its own with its tiles, its chart, its table with "Export
 * CSV", its skeleton, its empty text and its error.
 */
/** A work-time report without a finished task that had an estimate. */
function noEstimates(): WorkTimeReportDto {
  return aWorkTimeReport({
    estimatedCount: 0,
    estimatedSeconds: 0,
    workedSeconds: 0,
    deviation: null,
    overCount: 0,
    byUser: [],
    byProject: [],
    over: [],
  });
}

describe('ReportsPage', () => {
  let api: {
    completed: Read<CompletedReportDto>;
    cycleTime: Read<CycleTimeReportDto>;
    deadlines: Read<DeadlinesReportDto>;
    workTime: Read<WorkTimeReportDto>;
    workSchedule: ReturnType<typeof vi.fn<() => Observable<ReportWorkScheduleDto>>>;
    projects: ReturnType<typeof vi.fn<() => Observable<ReportProjectDto[]>>>;
    members: ReturnType<typeof vi.fn<(projectId: string) => Observable<ReportPersonDto[]>>>;
  };
  let charts: ChartConfiguration<'bar'>[];
  let harness: RouterTestingHarness;
  let loader: HarnessLoader;
  const today = new Date();
  const last30 = presetRange('last30', today);

  beforeEach(() => {
    charts = [];
    api = {
      completed: vi.fn(() => of(aCompletedReport())),
      cycleTime: vi.fn(() => of(aCycleTimeReport())),
      deadlines: vi.fn(() => of(aDeadlinesReport())),
      workTime: vi.fn(() => of(aWorkTimeReport())),
      workSchedule: vi.fn(() => of({ days: [1, 2, 3, 4, 5], start: '08:30', end: '16:30' })),
      projects: vi.fn(() =>
        of([
          { id: 'p1', name: 'Kanabol', key: 'KAN', archivedAt: null },
          { id: 'p3', name: 'Vechi', key: 'OLD', archivedAt: '2026-10-30T10:00:00Z' },
        ]),
      ),
      members: vi.fn(() =>
        of([
          { userId: 'u2', displayName: 'Dan Ionescu' },
          { userId: 'u4', displayName: 'Ioana Rus' },
        ]),
      ),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: Elsewhere },
          { path: 'reports', component: ReportsPage },
          {
            path: 'projects/:id',
            component: ProjectLike,
            children: [{ path: 'reports', component: ReportsPage, data: { projectFixed: true } }],
          },
        ]),
        { provide: ReportsApi, useValue: api },
        {
          provide: CHART_RENDERER,
          useValue: {
            draw: (_canvas: HTMLCanvasElement, config: ChartConfiguration<'bar'>): ChartHandle => {
              charts.push(config);
              return { update: (next) => charts.push(next), destroy: () => undefined };
            },
          },
        },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function open(address = '/reports'): Promise<HTMLElement> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(address);
    await settle();
    loader = TestbedHarnessEnvironment.loader(harness.fixture);
    return harness.routeNativeElement!;
  }

  // The charts come with @defer, on idle.
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await harness.fixture.whenStable();
  }

  function section(
    page: HTMLElement,
    name: 'completed' | 'durations' | 'deadlines' | 'work-time',
  ): HTMLElement {
    return page.querySelector<HTMLElement>(`section.${name}`)!;
  }

  function tiles(element: HTMLElement): string[] {
    return [...element.querySelectorAll('kb-stat-tile')].map((tile) =>
      [...tile.querySelectorAll('.label, .value, .caption')]
        .map((part) => part.textContent.trim().replace(/\s+/g, ' '))
        .join(' | '),
    );
  }

  function rows(table: Element): string[] {
    return [...table.querySelectorAll('tr')].map((row) =>
      [...row.querySelectorAll('th, td')]
        .map((cell) => cell.textContent.trim().replace(/\s+/g, ' '))
        .join(' | '),
    );
  }

  function url(): string {
    return TestBed.inject(Router).url;
  }

  it('is titled "Rapoarte", under Acasă', async () => {
    const page = await open();

    expect(page.querySelector('h1')?.textContent.trim()).toBe('Rapoarte');
    expect(page.querySelector('kb-page-header')?.textContent).toContain('Acasă');
  });

  it('opens on the last 30 days, by the week, for every project and everyone', async () => {
    const page = await open();

    const filters = { ...last30, project: null, user: null, granularity: 'week' };
    expect(api.completed).toHaveBeenCalledWith(filters);
    expect(api.cycleTime).toHaveBeenCalledWith(filters);
    expect(api.deadlines).toHaveBeenCalledWith(filters);
    const pressed = [...page.querySelectorAll('.kb-filter-chip[aria-pressed="true"]')].map((chip) =>
      chip.textContent.trim(),
    );
    expect(pressed).toEqual(['Ultimele 30 de zile']);
  });

  it('takes the filters from the address: from, to, project, user and granularity', async () => {
    await open('/reports?from=2026-10-01&to=2026-10-31&project=p1&user=u2&granularity=month');

    expect(api.completed).toHaveBeenLastCalledWith({
      from: '2026-10-01',
      to: '2026-10-31',
      project: 'p1',
      user: 'u2',
      granularity: 'month',
    });
    expect(
      await (
        await loader.getHarness(MatSelectHarness.with({ selector: '.project' }))
      ).getValueText(),
    ).toBe('Kanabol');
    expect(
      await (
        await loader.getHarness(MatSelectHarness.with({ selector: '.granularity' }))
      ).getValueText(),
    ).toBe('Lună');
  });

  it('sends a wrong interval in the address back to the last 30 days', async () => {
    await open('/reports?from=2026-10-31&to=2026-10-01&project=p1');

    expect(url()).toBe('/reports?project=p1');
    expect(api.completed).toHaveBeenLastCalledWith({
      ...last30,
      project: 'p1',
      user: null,
      granularity: 'week',
    });
    expect(api.completed.mock.calls.every(([filters]) => filters.from === last30.from)).toBe(true);
  });

  it('puts a preset in the address, and the last 30 days out of it', async () => {
    const page = await open();
    const chip = (text: string) =>
      [...page.querySelectorAll<HTMLButtonElement>('.kb-filter-chip')].find(
        (one) => one.textContent.trim() === text,
      )!;

    chip('Săptămâna asta').click();
    await settle();
    const week = presetRange('week', today);
    expect(url()).toBe(`/reports?from=${week.from}&to=${week.to}`);
    expect(chip('Săptămâna asta').getAttribute('aria-pressed')).toBe('true');

    chip('Ultimele 30 de zile').click();
    await settle();
    expect(url()).toBe('/reports');
  });

  it('bounds the dates to each other and to 366 days', async () => {
    await open('/reports?from=2026-10-01&to=2026-10-31');
    const from = await loader.getHarness(
      MatDatepickerInputHarness.with({ selector: '.from input' }),
    );
    const to = await loader.getHarness(MatDatepickerInputHarness.with({ selector: '.to input' }));

    expect(await from.getValue()).toBe('01.10.2026');
    expect(await to.getValue()).toBe('31.10.2026');
    // The input's min and max attributes are written in UTC by the native adapter: the directive's own dates.
    const bounds = (selector: string) => {
      const input = harness.fixture.debugElement
        .query(By.css(selector))
        .injector.get(MatDatepickerInput<Date>);
      return [dayOf(input.min!), dayOf(input.max!)];
    };
    expect(bounds('.from input')).toEqual(['2025-10-31', '2026-10-31']);
    expect(bounds('.to input')).toEqual(['2026-10-01', '2027-10-01']);
  });

  it('puts a day chosen in a datepicker in the address', async () => {
    await open('/reports?from=2026-10-01&to=2026-10-31');
    const toggle = await loader.getHarness(
      MatDatepickerToggleHarness.with({ selector: '.from mat-datepicker-toggle' }),
    );

    await toggle.openCalendar();
    const calendar = await TestbedHarnessEnvironment.documentRootLoader(harness.fixture).getHarness(
      MatCalendarHarness,
    );
    await calendar.selectCell({ text: '15' });
    await settle();

    expect(url()).toBe('/reports?from=2026-10-15&to=2026-10-31');
  });

  it('chooses a project, a person and the month through the address, the person going with the project', async () => {
    await open('/reports?user=u3');

    await (
      await loader.getHarness(MatSelectHarness.with({ selector: '.project' }))
    ).clickOptions({ text: /Kanabol/ });
    await settle();
    expect(url()).toBe('/reports?project=p1');

    await (
      await loader.getHarness(MatSelectHarness.with({ selector: '.user' }))
    ).clickOptions({ text: 'Ioana Rus' });
    await (
      await loader.getHarness(MatSelectHarness.with({ selector: '.granularity' }))
    ).clickOptions({ text: 'Lună' });
    await settle();
    expect(url()).toBe('/reports?project=p1&user=u4&granularity=month');
  });

  it('offers every project the person sees, the archived ones said so', async () => {
    await open();
    const select = await loader.getHarness(MatSelectHarness.with({ selector: '.project' }));

    await select.open();
    expect(
      await Promise.all((await select.getOptions()).map((option) => option.getText())),
    ).toEqual(['Toate proiectele', 'Kanabol', 'Vechi (arhivat)']);
  });

  it("offers the project's members, and without a project the report's people", async () => {
    await open();
    const select = await loader.getHarness(MatSelectHarness.with({ selector: '.user' }));
    await select.open();
    expect(
      await Promise.all((await select.getOptions()).map((option) => option.getText())),
    ).toEqual(['Oricine', 'Ana Pop', 'Dan Ionescu', 'Eva Marin']);
    await select.close();

    await harness.navigateByUrl('/reports?project=p1');
    await settle();
    expect(api.members).toHaveBeenCalledWith('p1');
    await select.open();
    expect(
      await Promise.all((await select.getOptions()).map((option) => option.getText())),
    ).toEqual(['Oricine', 'Dan Ionescu', 'Ioana Rus']);
  });

  it("falls back on the report's people when the members can't be read", async () => {
    api.members.mockReturnValue(throwError(() => new Error('403')));
    await open('/reports?project=p1');
    const select = await loader.getHarness(MatSelectHarness.with({ selector: '.user' }));

    await select.open();
    expect(
      await Promise.all((await select.getOptions()).map((option) => option.getText())),
    ).toEqual(['Oricine', 'Ana Pop', 'Dan Ionescu', 'Eva Marin']);
  });

  it('shows the finished tasks: the total, by person with its note, by period, person and project', async () => {
    const completed = section(await open(), 'completed');

    expect(completed.querySelector('h2')?.textContent.trim()).toBe('Task-uri finalizate');
    expect(tiles(completed)).toEqual([
      'Finalizate | 7 | în interval',
      'Pe om | 8 | un task cu mai mulți asignați se numără la fiecare',
    ]);
    const [periods, people, projects] = [...completed.querySelectorAll('table')];
    expect(rows(periods)).toEqual([
      'Perioada | Finalizate',
      '5 oct. 2026 | 2',
      '12 oct. 2026 | 2',
      '19 oct. 2026 | 1',
      '26 oct. 2026 | 2',
    ]);
    expect(rows(people)).toEqual([
      'Pe om | Finalizate',
      'Dan Ionescu | 5',
      'Ana Pop | 2',
      'Eva Marin | 1',
    ]);
    expect(rows(projects)).toEqual(['Pe proiect | Finalizate', 'Kanabol KAN | 4', 'Site WEB | 3']);
  });

  it('names a period by the month with month granularity', async () => {
    api.completed.mockReturnValue(
      of(aCompletedReport({ series: [{ periodStart: '2026-10-01T00:00:00+03:00', count: 7 }] })),
    );
    const completed = section(await open('/reports?granularity=month'), 'completed');

    expect(rows(completed.querySelector('table')!)[1]).toBe('octombrie 2026 | 7');
  });

  it('shows lead and cycle time as "2 z 4 h", with the median in the caption', async () => {
    const durations = section(await open(), 'durations');

    expect(tiles(durations)).toEqual([
      'Lead time mediu | 5 z 19 h | mediana 5 z 2 h',
      'Cycle time mediu | 3 z 15 h | mediana 2 z',
    ]);
    expect(rows(durations.querySelector('table')!)).toEqual([
      'Măsura | Media | Mediana',
      'Lead time | 5 z 19 h | 5 z 2 h',
      'Cycle time | 3 z 15 h | 2 z',
    ]);
  });

  it("shows the time in each of the project's columns with a project", async () => {
    api.cycleTime.mockReturnValue(
      of(
        aCycleTimeReport({
          perColumn: [
            { columnId: 'c1', name: 'De făcut', averageSeconds: 291_600, medianSeconds: 266_400 },
            { columnId: 'c2', name: 'În lucru', averageSeconds: 2_100, medianSeconds: null },
          ],
        }),
      ),
    );
    const durations = section(await open('/reports?project=p1'), 'durations');

    expect(rows(durations.querySelector('table')!)).toEqual([
      'Coloana | Media | Mediana',
      'De făcut | 3 z 9 h | 3 z 2 h',
      'În lucru | 35 min | –',
    ]);
  });

  it('shows the deadlines: overdue now, on time, late and the rate as a percent', async () => {
    const deadlines = section(await open(), 'deadlines');

    expect(tiles(deadlines)).toEqual([
      'Întârziate acum | 2 | deschise, după deadline',
      'La timp | 6 | finalizate până la deadline',
      'După termen | 2 | finalizate după deadline',
      'Rata de respectare | 75 % | la timp, din cele cu deadline',
    ]);
    expect(rows(deadlines.querySelector('table')!)).toEqual([
      'Perioada | La timp | După termen',
      '5 oct. 2026 | 1 | 1',
      '12 oct. 2026 | 2 | 1',
      '19 oct. 2026 | 0 | 0',
      '26 oct. 2026 | 3 | 0',
    ]);
  });

  it('draws the three charts: by period, the times with mean and median, and on time stacked on late', async () => {
    await open();

    const [completed, durations, deadlines] = charts;
    expect(completed.data.labels).toEqual(['5 oct.', '12 oct.', '19 oct.', '26 oct.']);
    expect(completed.data.datasets.map((dataset) => [dataset.label, dataset.data])).toEqual([
      ['Finalizate', [2, 2, 1, 2]],
    ]);
    expect(durations.data.labels).toEqual(['Lead time', 'Cycle time']);
    expect(durations.data.datasets.map((dataset) => dataset.label)).toEqual(['Media', 'Mediana']);
    expect(durations.data.datasets[1].data).toEqual([439_200 / 3600, 2 * 24]);
    expect(deadlines.options!.scales!['x']!.stacked).toBe(true);
    expect(deadlines.data.datasets.map((dataset) => [dataset.label, dataset.data])).toEqual([
      ['La timp', [1, 2, 0, 3]],
      ['După termen', [1, 1, 0, 0]],
    ]);
  });

  it('exports the data of a section as CSV, named by what it holds and its days', async () => {
    const blobs: Blob[] = [];
    Object.assign(URL, {
      createObjectURL: vi.fn((blob: Blob) => (blobs.push(blob), 'blob:report')),
      revokeObjectURL: vi.fn(),
    });
    const names: string[] = [];
    const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      names.push(this.download);
    });
    const page = await open('/reports?from=2026-10-05&to=2026-11-01');

    for (const name of ['completed', 'durations', 'deadlines'] as const) {
      section(page, name).querySelector<HTMLButtonElement>('header button')!.click();
    }

    expect(names).toEqual([
      'kanabol-finalizate-2026-10-05-2026-11-01.csv',
      'kanabol-durate-2026-10-05-2026-11-01.csv',
      'kanabol-deadline-uri-2026-10-05-2026-11-01.csv',
    ]);
    // Blob.text() drops a BOM: the bytes are read as they are.
    const text = async (blob: Blob) =>
      new TextDecoder('utf-8', { ignoreBOM: true }).decode(await blob.arrayBuffer());
    expect(await text(blobs[0])).toBe(
      '\uFEFF"Perioada","Finalizate"\r\n"2026-10-05","2"\r\n"2026-10-12","2"\r\n"2026-10-19","1"\r\n"2026-10-26","2"\r\n',
    );
    expect(await text(blobs[1])).toBe(
      '\uFEFF"Măsura","Media (secunde)","Mediana (secunde)"\r\n"Lead time","502457","439200"\r\n"Cycle time","315771","172800"\r\n',
    );
    expect((await blobs[2].text()).split('\r\n')[1]).toBe('"2026-10-05","1","1"');
    clicked.mockRestore();
  });

  it('says "Niciun task finalizat în interval." and "Niciun task cu deadline în interval."', async () => {
    api.workTime.mockReturnValue(of(noEstimates()));
    api.completed.mockReturnValue(of(aCompletedReport({ total: 0, byUser: [], byProject: [] })));
    api.cycleTime.mockReturnValue(
      of(
        aCycleTimeReport({
          count: 0,
          leadTime: { averageSeconds: null, medianSeconds: null },
          cycleTime: { averageSeconds: null, medianSeconds: null },
        }),
      ),
    );
    api.deadlines.mockReturnValue(
      of(aDeadlinesReport({ completedOnTime: 0, completedLate: 0, onTimeRate: null })),
    );
    const page = await open();

    expect(section(page, 'completed').querySelector('kb-empty-state')?.textContent).toContain(
      'Niciun task finalizat în interval.',
    );
    expect(section(page, 'durations').querySelector('kb-empty-state')?.textContent).toContain(
      'Niciun task finalizat în interval.',
    );
    expect(section(page, 'deadlines').querySelector('kb-empty-state')?.textContent).toContain(
      'Niciun task cu deadline în interval.',
    );
    expect(tiles(section(page, 'durations'))[0]).toBe('Lead time mediu | – | mediana –');
    expect(tiles(section(page, 'deadlines'))[3]).toBe(
      'Rata de respectare | – | la timp, din cele cu deadline',
    );
    expect(section(page, 'work-time').querySelector('kb-empty-state')?.textContent).toContain(
      'Niciun task finalizat cu estimare în acest interval.',
    );
    expect(page.querySelectorAll('table')).toHaveLength(0);
    expect(charts).toHaveLength(0);
  });

  it('shows a skeleton in each section while it reads', async () => {
    api.completed.mockReturnValue(NEVER);
    api.cycleTime.mockReturnValue(NEVER);
    api.deadlines.mockReturnValue(NEVER);
    const page = await open();

    for (const name of ['completed', 'durations', 'deadlines'] as const) {
      expect(
        section(page, name).querySelector('.placeholder[aria-busy="true"] kb-skeleton'),
      ).not.toBeNull();
      expect(section(page, name).querySelector('kb-stat-tile .value kb-skeleton')).not.toBeNull();
    }
  });

  it('says a section could not be read, and tries that one again', async () => {
    api.deadlines.mockReturnValueOnce(throwError(() => new Error('500')));
    const page = await open();
    const deadlines = section(page, 'deadlines');

    expect(deadlines.querySelector('kb-empty-state')?.textContent).toContain(
      'Nu am putut încărca raportul.',
    );
    expect(section(page, 'completed').querySelector('kb-empty-state')).toBeNull();

    deadlines.querySelector<HTMLButtonElement>('kb-empty-state button')!.click();
    await settle();
    expect(api.deadlines).toHaveBeenCalledTimes(2);
    expect(api.completed).toHaveBeenCalledTimes(1);
    expect(tiles(section(page, 'deadlines'))[0]).toBe(
      'Întârziate acum | 2 | deschise, după deadline',
    );
  });

  // docs/UI_UX_SPEC.md §7.3: the project's "Rapoarte" tab is the same page, its project fixed.
  describe('in a project', () => {
    it('reads the project of the route, whatever the address says, without a title, a breadcrumb or a project select', async () => {
      const page = await open('/projects/p1/reports?project=p9');

      expect(api.completed).toHaveBeenLastCalledWith({
        ...last30,
        project: 'p1',
        user: null,
        granularity: 'week',
      });
      expect(api.cycleTime).toHaveBeenLastCalledWith({
        ...last30,
        project: 'p1',
        user: null,
        granularity: 'week',
      });
      expect(page.querySelector('kb-page-header')).toBeNull();
      expect(page.querySelector('h1')).toBeNull();
      expect(page.querySelector('.project')).toBeNull();
      expect(page.querySelectorAll('section.report')).toHaveLength(4);
      expect(api.workTime).toHaveBeenLastCalledWith({
        ...last30,
        project: 'p1',
        user: null,
        granularity: 'week',
      });
    });

    it('keeps the project out of the address', async () => {
      await open('/projects/p1/reports');

      await (
        await loader.getHarness(MatSelectHarness.with({ selector: '.user' }))
      ).clickOptions({ text: 'Ioana Rus' });
      await settle();
      expect(url()).toBe('/projects/p1/reports?user=u4');
    });

    it("reads the project's members for the user select, and no list of projects", async () => {
      await open('/projects/p1/reports');

      expect(api.members).toHaveBeenCalledWith('p1');
      expect(api.projects).not.toHaveBeenCalled();
    });

    it('follows the route to another project', async () => {
      await open('/projects/p1/reports');

      await harness.navigateByUrl('/projects/p2/reports');
      await settle();

      expect(api.completed).toHaveBeenLastCalledWith({
        ...last30,
        project: 'p2',
        user: null,
        granularity: 'week',
      });
    });
  });
  // docs/UI_UX_SPEC.md §7.8 (group 22): the fourth section, "Estimare și timp lucrat".
  describe('Estimare și timp lucrat', () => {
    function workTime(page: HTMLElement): HTMLElement {
      return page.querySelector<HTMLElement>('section.work-time')!;
    }

    function rows(table: Element): string[] {
      return [...table.querySelectorAll('tr.kb-table-row')].map((row) =>
        [...row.querySelectorAll('td')]
          .map((cell) => cell.textContent.trim().replace(/\s+/g, ' '))
          .join(' | '),
      );
    }

    it('comes fourth, with the schedule of now under its title', async () => {
      const page = await open();

      expect(
        [...page.querySelectorAll('section.report h2')].map((title) => title.textContent.trim()),
      ).toEqual([
        'Task-uri finalizate',
        'Lead time și cycle time',
        'Deadline-uri',
        'Estimare și timp lucrat',
      ]);
      expect(workTime(page).querySelector('.note')?.textContent.trim()).toBe(
        'Timpul lucrat se numără doar în programul de lucru (luni–vineri, 08:30–16:30).',
      );
      expect(api.workTime).toHaveBeenCalledWith(
        expect.objectContaining({ from: last30.from, to: last30.to }),
      );
    });

    it('names days that do not follow each other one by one', async () => {
      api.workSchedule.mockReturnValue(of({ days: [1, 3, 5], start: '09:00', end: '17:00' }));
      const page = await open();

      expect(workTime(page).querySelector('.note')?.textContent).toContain(
        '(luni, miercuri, vineri, 09:00–17:00)',
      );
    });

    it('shows Estimat, Lucrat, Abatere and Peste estimare, the deviation with its sign', async () => {
      const page = await open();

      expect(tiles(workTime(page))).toEqual([
        'Estimat | 12 h | al task-urilor estimate și lucrate',
        'Lucrat | 17 h | în programul de lucru',
        'Abatere | +42 % | lucrat față de estimat',
        'Peste estimare | 2 | task-uri',
      ]);
    });

    it('writes a deviation under the estimate with a true minus, and none as a dash', async () => {
      api.workTime.mockReturnValue(of(aWorkTimeReport({ deviation: -0.05 })));
      const page = await open();

      expect(tiles(workTime(page))[2]).toBe('Abatere | −5 % | lucrat față de estimat');
      const [people, projects] = [
        ...workTime(page).querySelectorAll('table.people, table.projects'),
      ];
      expect(rows(people)).toEqual([
        'Dan Ionescu | 2 | 10 h | 16 h | +60 %',
        'Ana Pop | 1 | 2 h | 1 h | −50 %',
      ]);
      expect(rows(projects)).toEqual([
        'Kanabol KAN | 2 | 10 h | 16 h | +60 %',
        'Site WEB | 1 | 0 min | 0 min | —',
      ]);
    });

    // docs/UI_UX_SPEC.md §7.8 (group 23): this section's durations are hours of the schedule, never days.
    it('writes its durations in hours and minutes only, a day and more too, while the rest keeps its days', async () => {
      api.workTime.mockReturnValue(
        of(
          aWorkTimeReport({
            estimatedSeconds: 40 * 3600 + 30 * 60,
            workedSeconds: 50 * 3600,
            byUser: [
              {
                userId: 'u2',
                name: 'Dan Ionescu',
                count: 2,
                estimatedSeconds: 26 * 3600,
                workedSeconds: 50 * 3600,
              },
            ],
            byProject: [
              {
                projectId: 'p1',
                key: 'KAN',
                name: 'Kanabol',
                count: 2,
                estimatedSeconds: 26 * 3600,
                workedSeconds: 50 * 3600,
              },
            ],
            over: [
              {
                taskId: 't1',
                key: 'KAN-1',
                title: 'Login SSO',
                estimatedSeconds: 24 * 3600,
                workedSeconds: 49 * 3600 + 15 * 60,
              },
            ],
          }),
        ),
      );
      const page = await open();

      expect(tiles(workTime(page)).slice(0, 2)).toEqual([
        'Estimat | 40 h 30 min | al task-urilor estimate și lucrate',
        'Lucrat | 50 h | în programul de lucru',
      ]);
      const [people, projects] = [
        ...workTime(page).querySelectorAll('table.people, table.projects'),
      ];
      expect(rows(people)[0]).toBe('Dan Ionescu | 2 | 26 h | 50 h | +92 %');
      expect(rows(projects)[0]).toBe('Kanabol KAN | 2 | 26 h | 50 h | +92 %');
      expect(rows(workTime(page).querySelector('table.over')!)).toEqual([
        'KAN-1 Login SSO | 24 h | 49 h 15 min | +25 h 15 min',
      ]);
      expect(workTime(page).textContent).not.toMatch(/\d z\b/);
    });

    it('draws estimated and worked by person, in hours, in the charts colors', async () => {
      const page = await open();
      await settle();
      expect(workTime(page).querySelector('kb-chart')).not.toBeNull();

      const chart = charts.find((config) => config.data.datasets[0]?.label === 'Estimat')!;
      expect(chart.data.labels).toEqual(['Dan Ionescu', 'Ana Pop']);
      expect(chart.data.datasets.map((dataset) => [dataset.label, dataset.data])).toEqual([
        ['Estimat', [10, 2]],
        ['Lucrat', [16, 1]],
      ]);
    });

    it('lists the tasks over their estimate, at most 20, each opening its task', async () => {
      const page = await open();

      const over = workTime(page).querySelector('table.over')!;
      expect(workTime(page).querySelector('#kb-report-over')?.textContent.trim()).toBe(
        'Task-uri peste estimare',
      );
      expect(rows(over)).toEqual([
        'KAN-1 Login SSO | 4 h | 10 h | +6 h',
        'WEB-3 Formular | 1 h | 1 h 30 min | +30 min',
      ]);

      const navigated = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      over.querySelector<HTMLButtonElement>('.task-link')!.click();

      expect(navigated).toHaveBeenCalledWith(
        [{ outlets: { panel: ['task', 'KAN-1'] } }],
        expect.objectContaining({ queryParamsHandling: 'preserve' }),
      );
    });

    it('says when no finished task had an estimate', async () => {
      api.workTime.mockReturnValue(
        of(
          aWorkTimeReport({
            estimatedCount: 0,
            estimatedSeconds: 0,
            workedSeconds: 0,
            deviation: null,
            overCount: 0,
            over: [],
          }),
        ),
      );
      const page = await open();

      expect(workTime(page).querySelector('kb-empty-state')?.textContent).toContain(
        'Niciun task finalizat cu estimare în acest interval.',
      );
      expect(workTime(page).querySelector<HTMLButtonElement>('header button')?.disabled).toBe(true);
    });

    it('exports the tables as CSV, named by what it holds and its days', async () => {
      const blobs: Blob[] = [];
      Object.assign(URL, {
        createObjectURL: vi.fn((blob: Blob) => (blobs.push(blob), 'blob:report')),
        revokeObjectURL: vi.fn(),
      });
      const names: string[] = [];
      const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
        this: HTMLAnchorElement,
      ) {
        names.push(this.download);
      });
      const page = await open('/reports?from=2026-10-05&to=2026-11-01');

      workTime(page).querySelector<HTMLButtonElement>('header button')!.click();

      expect(names).toEqual(['kanabol-estimare-2026-10-05-2026-11-01.csv']);
      const text = new TextDecoder('utf-8', { ignoreBOM: true }).decode(
        await blobs[0].arrayBuffer(),
      );
      expect(text.split('\r\n').slice(0, 3)).toEqual([
        '﻿"Tabel","Nume","Task-uri","Estimat (secunde)","Lucrat (secunde)","Abatere"',
        '"Pe om","Dan Ionescu","2","36000","57600","+60 %"',
        '"Pe om","Ana Pop","1","7200","3600","−50 %"',
      ]);
      clicked.mockRestore();
    });

    it('reads only its own section again on Reîncearcă', async () => {
      api.workTime.mockReturnValueOnce(throwError(() => new Error('down')));
      const page = await open();
      expect(workTime(page).textContent).toContain('Nu am putut încărca raportul.');

      [...workTime(page).querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent.trim() === 'Reîncearcă')!
        .click();
      await settle();

      expect(api.workTime).toHaveBeenCalledTimes(2);
      expect(api.completed).toHaveBeenCalledTimes(1);
      expect(tiles(workTime(page))[0]).toBe('Estimat | 12 h | al task-urilor estimate și lucrate');
    });
  });
});
