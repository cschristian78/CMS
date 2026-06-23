import { regionBarColor } from '../utils/regionBarColors';

export interface BarChartItem {
  label: string;
  value: number;
  /** Business unit code/name for US / UK / LatAM coloring. */
  colorKey?: string | null;
}

interface DashboardBarChartProps {
  title: string;
  items: BarChartItem[];
  ariaLabel: string;
  orientation?: 'vertical' | 'horizontal';
}

export default function DashboardBarChart({
  title,
  items,
  ariaLabel,
  orientation = 'vertical',
}: DashboardBarChartProps) {
  const plotHeightPx = 160;
  const max = items.reduce((m, x) => {
    const v = Number(x.value);
    return Number.isFinite(v) && v > m ? v : m;
  }, 0);

  const barHeightPx = (value: number): number => {
    const v = Number.isFinite(value) ? Math.max(0, value) : 0;
    if (max <= 0) {
      return v > 0 ? 12 : 6;
    }
    return Math.max(v > 0 ? 8 : 4, Math.round((v / max) * plotHeightPx));
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
              const fill = regionBarColor(item.colorKey ?? item.label);
              return (
                <div className="bar-row" key={`${item.label}-${item.colorKey ?? ''}`}>
                  <span className="bar-label">{item.label}</span>
                  <div className="bar-track" aria-hidden>
                    <div className="bar-fill" style={{ width: `${widthPct}%`, background: fill }} />
                  </div>
                  <span className="bar-value">{item.value}</span>
                </div>
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
              const fill = regionBarColor(item.colorKey ?? item.label);
              const h = barHeightPx(item.value);
              return (
                <div className="bar-column" key={`${item.label}-${item.colorKey ?? ''}`}>
                  <span className="bar-column-value">{item.value}</span>
                  <div className="bar-column-track" aria-hidden>
                    <div
                      className="bar-column-fill"
                      style={{ height: `${h}px`, background: fill }}
                    />
                  </div>
                  <span className="bar-column-label">{item.label}</span>
                </div>
              );
            })}
          </div>
        </div>
      }
    </section>
  );
}
