import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ChartConfiguration, TooltipModel } from 'chart.js';
import { CHART_RENDERER, Chart, ChartHandle, ChartSeries } from './chart';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Chart],
  template: `
    <kb-chart
      label="Task-uri finalizate pe săptămână"
      [labels]="labels()"
      [series]="series()"
      [stacked]="stacked()"
      [formatValue]="formatValue"
    />
  `,
})
class Host {
  readonly labels = signal<readonly string[]>(['5 oct.', '12 oct.']);
  readonly series = signal<readonly ChartSeries[]>([
    { label: 'Finalizate', values: [2, 3], tone: 'primary' },
  ]);
  readonly stacked = signal(false);
  readonly formatValue = (value: number) => `${String(value)} task-uri`;
}

type BarConfig = ChartConfiguration<'bar'>;

/**
 * docs/UI_UX_SPEC.md §7.8: bars over a canvas, the main series --kb-chart-1 and the second --kb-chart-2 (§2.1,
 * §2.3; group 19), the grid border-200, no 3D or gradient, tooltips in the snackbar's style; the colors come
 * from the theme's tokens.
 */
describe('Chart', () => {
  let drawn: { canvas: HTMLCanvasElement; config: BarConfig }[];
  let updates: BarConfig[];
  let destroyed: number;

  beforeEach(() => {
    drawn = [];
    updates = [];
    destroyed = 0;
    const root = document.documentElement.style;
    root.setProperty('--kb-chart-1', 'rgb(18, 45, 148)');
    root.setProperty('--kb-chart-2', 'rgb(19, 91, 247)');
    // What the charts read before group 19, set apart so a chart still reading them shows.
    root.setProperty('--kb-brand-800', 'rgb(1, 2, 3)');
    root.setProperty('--kb-sky-300', 'rgb(4, 5, 6)');
    root.setProperty('--kb-border-200', 'rgb(229, 232, 238)');
    root.setProperty('--kb-text-600', 'rgb(97, 105, 113)');
    root.setProperty('--kb-surface-0', 'rgb(255, 255, 255)');
    TestBed.configureTestingModule({
      providers: [
        {
          provide: CHART_RENDERER,
          useValue: {
            draw: (canvas: HTMLCanvasElement, config: BarConfig): ChartHandle => {
              drawn.push({ canvas, config });
              return { update: (next) => updates.push(next), destroy: () => destroyed++ };
            },
          },
        },
      ],
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return {
      fixture,
      host: fixture.componentInstance,
      element: fixture.nativeElement as HTMLElement,
    };
  }

  it('draws bars on a canvas described for screen readers', async () => {
    const { element } = await render();

    expect(drawn).toHaveLength(1);
    const canvas = element.querySelector('canvas')!;
    expect(drawn[0].canvas).toBe(canvas);
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toBe('Task-uri finalizate pe săptămână');
    expect(drawn[0].config.type).toBe('bar');
    expect(drawn[0].config.data.labels).toEqual(['5 oct.', '12 oct.']);
    expect(drawn[0].config.data.datasets[0].data).toEqual([2, 3]);
  });

  it('colors the main series --kb-chart-1 and the second --kb-chart-2, from the tokens', async () => {
    const { fixture, host } = await render();
    host.series.set([
      { label: 'Media', values: [1, 2], tone: 'primary' },
      { label: 'Mediana', values: [1, 1], tone: 'secondary' },
    ]);

    const config = await latest(fixture);
    expect(config.data.datasets.map((dataset) => dataset.backgroundColor)).toEqual([
      'rgb(18, 45, 148)',
      'rgb(19, 91, 247)',
    ]);
  });

  it('draws a recessive grid in border-200, the labels in text-600, and no grid across the bars', async () => {
    await render();
    const scales = drawn[0].config.options!.scales!;

    expect((scales['y']!.grid as { color: string }).color).toBe('rgb(229, 232, 238)');
    expect((scales['x']!.grid as { display: boolean }).display).toBe(false);
    expect((scales['y']!.ticks as { color: string }).color).toBe('rgb(97, 105, 113)');
    expect((scales['y'] as { beginAtZero: boolean }).beginAtZero).toBe(true);
  });

  it('shows a legend only for two series or more', async () => {
    const { fixture, host } = await render();
    expect(drawn[0].config.options!.plugins!.legend!.display).toBe(false);

    host.series.set([
      { label: 'La timp', values: [1, 0], tone: 'primary' },
      { label: 'După termen', values: [0, 1], tone: 'secondary' },
    ]);
    const config = await latest(fixture);
    expect(config.options!.plugins!.legend!.display).toBe(true);
  });

  it('stacks the bars when asked, with a gap in the surface between the pieces', async () => {
    const { fixture, host } = await render();
    host.stacked.set(true);
    host.series.set([
      { label: 'La timp', values: [1, 0], tone: 'primary' },
      { label: 'După termen', values: [0, 1], tone: 'secondary' },
    ]);

    const config = await latest(fixture);
    expect(config.options!.scales!['x']!.stacked).toBe(true);
    expect(config.options!.scales!['y']!.stacked).toBe(true);
    expect(config.data.datasets[1].borderColor).toBe('rgb(255, 255, 255)');
  });

  it('changes the chart it drew when the data changes, and draws no other', async () => {
    const { fixture, host } = await render();
    host.labels.set(['5 oct.', '12 oct.', '19 oct.']);
    host.series.set([{ label: 'Finalizate', values: [2, 3, 1], tone: 'primary' }]);

    const config = await latest(fixture);
    expect(drawn).toHaveLength(1);
    expect(config.data.datasets[0].data).toEqual([2, 3, 1]);
  });

  it('lets the chart go with the component', async () => {
    const { fixture } = await render();

    fixture.destroy();

    expect(destroyed).toBe(1);
  });

  it('writes its tooltip as text, the period and each series with its value', async () => {
    const { element } = await render();
    const external = drawn[0].config.options!.plugins!.tooltip!.external!;

    external.call(undefined as never, {
      chart: undefined as never,
      tooltip: {
        opacity: 1,
        caretX: 40,
        caretY: 60,
        title: ['12 oct.'],
        dataPoints: [{ dataset: { label: 'Finalizate' }, raw: 3 }],
      } as unknown as TooltipModel<'bar'>,
    });

    const tooltip = element.querySelector<HTMLElement>('.kb-chart-tooltip')!;
    expect(tooltip.hidden).toBe(false);
    expect(tooltip.textContent).toBe('12 oct.Finalizate: 3 task-uri');
    expect(drawn[0].config.options!.plugins!.tooltip!.enabled).toBe(false);
  });

  async function latest(fixture: { whenStable: () => Promise<unknown> }): Promise<BarConfig> {
    await fixture.whenStable();
    return updates.at(-1) ?? drawn[0].config;
  }
});
