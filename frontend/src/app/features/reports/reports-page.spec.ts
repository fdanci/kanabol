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
import { Router, provideRouter } from '@angular/router';
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
} from './models';
import { ReportFilters, dayOf, presetRange } from './report-filters';
import { ReportsApi } from './reports-api';
import { ReportsPage } from './reports-page';
import { aCompletedReport, aCycleTimeReport, aDeadlinesReport } from './reports-testing';

@Component({ changeDetection: ChangeDetectionStrategy.OnPush, template: `` })
class Elsewhere {}

type Read<T> = ReturnType<typeof vi.fn<(filters: ReportFilters) => Observable<T>>>;

/**
 * docs/UI_UX_SPEC.md §7.8: the filters' card, every filter in the address; "Task-uri finalizate", "Lead time
 * și cycle time" and "Deadline-uri", each read on its own with its tiles, its chart, its table with "Export
 * CSV", its skeleton, its empty text and its error.
 */
describe('ReportsPage', () => {
  let api: {
    completed: Read<CompletedReportDto>;
    cycleTime: Read<CycleTimeReportDto>;
    deadlines: Read<DeadlinesReportDto>;
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

  function section(page: HTMLElement, name: 'completed' | 'durations' | 'deadlines'): HTMLElement {
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
});
