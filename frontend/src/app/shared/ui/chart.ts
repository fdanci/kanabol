import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  afterNextRender,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import {
  BarController,
  BarElement,
  CategoryScale,
  type ChartConfiguration,
  Chart as ChartJs,
  Legend,
  LinearScale,
  type TooltipModel,
  Tooltip,
} from 'chart.js';

// Only what the reports' bars use, so the rest of chart.js stays out of the bundle (docs/SPEC.md §5.1.1).
ChartJs.register(BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip);

/** The main series is --kb-chart-1, the second --kb-chart-2 (docs/UI_UX_SPEC.md §2.1, §7.8). */
export type ChartTone = 'primary' | 'secondary';

export interface ChartSeries {
  label: string;
  /** One for each label; null leaves that bar out. */
  values: readonly (number | null)[];
  tone: ChartTone;
}

type BarConfiguration = ChartConfiguration<'bar', (number | null)[], string>;

export interface ChartHandle {
  update(config: BarConfiguration): void;
  destroy(): void;
}

/** Draws on the canvas: chart.js in the app, a fake in the specs, since jsdom has no canvas. */
export interface ChartRenderer {
  draw(canvas: HTMLCanvasElement, config: BarConfiguration): ChartHandle;
}

export const CHART_RENDERER = new InjectionToken<ChartRenderer>('CHART_RENDERER', {
  providedIn: 'root',
  factory: () => ({
    draw: (canvas, config) => {
      const chart = new ChartJs(canvas, config);
      return {
        update: (next) => {
          chart.data = next.data;
          chart.options = next.options ?? {};
          chart.update();
        },
        destroy: () => {
          chart.destroy();
        },
      };
    },
  }),
});

const NUMBER = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 1 });

/** A rounded end of 4px at the top of a bar, none where it meets the axis or another piece. */
const BAR_END = 4;

/**
 * A bar chart over a canvas (docs/UI_UX_SPEC.md §7.8), with chart.js 4: the main series --kb-chart-1, the
 * second --kb-chart-2, over 3:1 on the card in both themes (§2.2), a recessive grid in border-200, no 3D,
 * no gradient, a legend only for two series or more, and a tooltip in the snackbar's style, written as text
 * in an element of its own (`kb-chart-tooltip`, global for its white text). The colors come from the theme's
 * tokens, read again when the theme changes. It is loaded with @defer, so chart.js stays out of the first
 * bundle; the table under it is what a screen reader reads.
 */
@Component({
  selector: 'kb-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="frame">
      <canvas #canvas role="img" [attr.aria-label]="label()"></canvas>
      <div #tooltip class="kb-chart-tooltip" hidden></div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .frame {
      position: relative;
      height: 240px;
    }
  `,
})
export class Chart {
  private readonly renderer = inject(CHART_RENDERER);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly tooltip = viewChild.required<ElementRef<HTMLElement>>('tooltip');
  private handle: ChartHandle | null = null;
  private readonly theme = signal(0);

  /** What the chart shows, for the canvas's aria-label. */
  readonly label = input.required<string>();
  readonly labels = input.required<readonly string[]>();
  readonly series = input.required<readonly ChartSeries[]>();
  readonly stacked = input(false);
  /** A value in the tooltip. */
  readonly formatValue = input<(value: number) => string>((value) => NUMBER.format(value));
  /** A value on the axis. */
  readonly formatTick = input<(value: number) => string>((value) => NUMBER.format(value));

  constructor() {
    afterNextRender(() => {
      this.handle = this.renderer.draw(this.canvas().nativeElement, this.config());
    });

    effect(() => {
      this.labels();
      this.series();
      this.stacked();
      this.theme();
      untracked(() => this.handle?.update(this.config()));
    });

    this.followTheTheme();
    inject(DestroyRef).onDestroy(() => {
      this.handle?.destroy();
      this.handle = null;
    });
  }

  private config(): BarConfiguration {
    const style = getComputedStyle(this.host.nativeElement);
    const token = (name: string) => style.getPropertyValue(name).trim();
    const font = { family: token('--kb-font') || undefined, size: 12 };
    const text = token('--kb-text-600');
    const grid = token('--kb-border-200');
    const surface = token('--kb-surface-0');
    const stacked = this.stacked();
    const series = this.series();
    const formatValue = this.formatValue();
    const formatTick = this.formatTick();

    return {
      type: 'bar',
      data: {
        labels: [...this.labels()],
        datasets: series.map((one, index) => ({
          label: one.label,
          data: [...one.values],
          backgroundColor: token(one.tone === 'primary' ? '--kb-chart-1' : '--kb-chart-2'),
          // A 2px gap in the card's own color between pieces of a stack, and between bars side by side.
          borderColor: surface,
          borderWidth: stacked && index < series.length - 1 ? { top: 2 } : 0,
          borderRadius: stacked && index < series.length - 1 ? 0 : BAR_END,
          borderSkipped: 'start',
          maxBarThickness: 40,
          categoryPercentage: 0.7,
          barPercentage: series.length > 1 && !stacked ? 0.9 : 1,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: this.reducedMotion() ? false : { duration: 200 },
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: {
            display: series.length > 1,
            position: 'bottom',
            align: 'start',
            labels: {
              color: text,
              font,
              boxWidth: 10,
              boxHeight: 10,
              useBorderRadius: true,
              borderRadius: 2,
            },
          },
          tooltip: {
            enabled: false,
            external: ({ tooltip }) => {
              this.showTooltip(tooltip, formatValue);
            },
          },
        },
        scales: {
          x: {
            stacked,
            grid: { display: false },
            border: { color: grid },
            ticks: { color: text, font },
          },
          y: {
            stacked,
            beginAtZero: true,
            grid: { color: grid },
            border: { display: false },
            ticks: {
              color: text,
              font,
              precision: 0,
              callback: (value) => formatTick(Number(value)),
            },
          },
        },
      },
    };
  }

  /** The tooltip as text, never as HTML: the period, then each series with its value. */
  private showTooltip(tooltip: TooltipModel<'bar'>, formatValue: (value: number) => string): void {
    const element = this.tooltip().nativeElement;
    if (tooltip.opacity === 0) {
      element.hidden = true;
      return;
    }
    const title = document.createElement('div');
    title.className = 'title';
    title.textContent = tooltip.title.join(' ');
    const lines = tooltip.dataPoints.map((point) => {
      const line = document.createElement('div');
      line.textContent = `${point.dataset.label ?? ''}: ${formatValue(Number(point.raw))}`;
      return line;
    });
    element.replaceChildren(title, ...lines);
    element.style.left = `${String(tooltip.caretX)}px`;
    element.style.top = `${String(tooltip.caretY)}px`;
    element.hidden = false;
  }

  private reducedMotion(): boolean {
    return (
      typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches
    );
  }

  /** The tokens change with the system's dark mode and with data-theme on the page: the colors are read again. */
  private followTheTheme(): void {
    const changed = () => {
      this.theme.update((count) => count + 1);
    };
    const scheme =
      typeof matchMedia === 'function' ? matchMedia('(prefers-color-scheme: dark)') : null;
    scheme?.addEventListener('change', changed);
    const observer = new MutationObserver(changed);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme'],
    });
    inject(DestroyRef).onDestroy(() => {
      scheme?.removeEventListener('change', changed);
      observer.disconnect();
    });
  }
}
