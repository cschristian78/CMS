import { regionBarColor } from '../utils/regionBarColors';

export interface BarChartItem {
  label: string;
  value: number;
  /** Business unit code/name for US / UK / LatAM coloring. */
  colorKey?: string | null;
  fill?: string;
  displayValue?: string;
}

interface DashboardBarChartProps {
  title: string;
  items: BarChartItem[];
  ariaLabel: string;
  orientation?: 'vertical' | 'horizontal';
  onSelect?: (item: BarChartItem) => void;
}

export default function DashboardBarChart({
  title,
  items,
  ariaLabel,
  orientation = 'vertical',
  onSelect,
}: DashboardBarChartProps) {
  const plotHeightPx = 160;
  const max = items.reduce((m, x) => {
    const v = Number(x.value);
    return Number.isFinite(v) && v > m ? v : m;
  }, 0);

  const barHeightPx = (value: number): number => {
    const v = Number.isFinite(value) ? Math.max(0, value) : 0;
    if (v <= 0) {
      return 0;
    }
    if (max <= 0) {
      return 12;
    }
    return Math.max(8, Math.round((v / max) * plotHeightPx));
  };

  if (orientation === 'horizontal') {
    return (
      <section className="dashboard-chart-card" aria-label={ariaLabel}>
        <h2 className="dashboard-chart-title">{title}</h2>
        {items.length === 0 ?
          <p className="field-help" role="status">
            No data to display.
          </p>
        : <div className="bar-chart">
            {items.map((item) => {
              const widthPct = max > 0 ? Math.round((item.value / max) * 100) : 0;
              const fill = item.fill ?? regionBarColor(item.colorKey ?? item.label);
              const shown = item.displayValue ?? (item.value > 0 ? String(item.value) : '');
              return (
                <button
                  type="button"
                  className="bar-row bar-row--button"
                  key={`${item.label}-${item.colorKey ?? ''}`}
                  onClick={() => onSelect?.(item)}
                >
                  <span className="bar-label">{item.label}</span>
                  <div className="bar-track" aria-hidden>
                    {item.value > 0 ?
                      <div className="bar-fill" style={{ width: `${widthPct}%`, background: fill }} />
                    : null}
                  </div>
                  <span className="bar-value">{shown}</span>
                </button>
              );
            })}
          </div>
        }
      </section>
    );
  }

  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 ?
        <p className="field-help" role="status">
          No data to display.
        </p>
      : <div className="bar-chart bar-chart--vertical" role="img" aria-label={ariaLabel}>
          <div className="bar-chart-plot">
            {items.map((item) => {
              const fill = item.fill ?? regionBarColor(item.colorKey ?? item.label);
              const h = barHeightPx(item.value);
              const shown = item.displayValue ?? (item.value > 0 ? String(item.value) : '');
              return (
                <button
                  type="button"
                  className="bar-column bar-column--button"
                  key={`${item.label}-${item.colorKey ?? ''}`}
                  onClick={() => onSelect?.(item)}
                >
                  <span className="bar-column-value">{shown}</span>
                  <div className="bar-column-track" aria-hidden>
                    {item.value > 0 ?
                      <div
                        className="bar-column-fill"
                        style={{ height: `${h}px`, background: fill }}
                      />
                    : null}
                  </div>
                  <span className="bar-column-label">{item.label}</span>
                </button>
              );
            })}
          </div>
        </div>
      }
    </section>
  );
}
