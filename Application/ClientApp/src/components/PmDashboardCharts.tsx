import type { BarChartItem } from './DashboardBarChart';
import { formatNumber, formatUsd, seriesColor } from '../dashboard/dashboardColors';

interface SliceItem {
  label: string;
  value: number;
  fill?: string;
}

interface DonutChartProps {
  title: string;
  items: SliceItem[];
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

function polar(cx: number, cy: number, r: number, angle: number): { x: number; y: number } {
  const rad = ((angle - 90) * Math.PI) / 180;
  return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) };
}

function arcPath(cx: number, cy: number, r: number, start: number, end: number): string {
  const s = polar(cx, cy, r, end);
  const e = polar(cx, cy, r, start);
  const large = end - start > 180 ? 1 : 0;
  return `M ${s.x} ${s.y} A ${r} ${r} 0 ${large} 0 ${e.x} ${e.y}`;
}

export function DashboardDonutChart({ title, items, ariaLabel, onSelect }: DonutChartProps) {
  const total = items.reduce((s, x) => s + Math.max(0, x.value), 0);
  const cx = 56;
  const cy = 56;
  const r = 40;
  let cursor = 0;

  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 || total <= 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <div className="pm-donut">
          <svg viewBox="0 0 112 112" className="pm-donut-svg" role="img" aria-label={ariaLabel}>
            <circle cx={cx} cy={cy} r={r} fill="none" stroke="rgba(26,45,68,0.9)" strokeWidth="16" />
            {items.map((item, i) => {
              const frac = item.value / total;
              const sweep = frac * 360;
              const start = cursor;
              const end = cursor + sweep;
              cursor = end;
              if (sweep <= 0) {
                return null;
              }
              const fill = item.fill ?? seriesColor(i, item.label);
              if (sweep >= 359.9) {
                return (
                  <circle
                    key={item.label}
                    cx={cx}
                    cy={cy}
                    r={r}
                    fill="none"
                    stroke={fill}
                    strokeWidth="16"
                    className={onSelect ? 'pm-chart-hit' : undefined}
                    onClick={() => onSelect?.(item.label)}
                  />
                );
              }
              return (
                <path
                  key={item.label}
                  d={arcPath(cx, cy, r, start, end)}
                  fill="none"
                  stroke={fill}
                  strokeWidth="16"
                  className={onSelect ? 'pm-chart-hit' : undefined}
                  onClick={() => onSelect?.(item.label)}
                />
              );
            })}
            <text x={cx} y={cy - 4} textAnchor="middle" className="pm-donut-total">
              {formatNumber(total)}
            </text>
            <text x={cx} y={cy + 12} textAnchor="middle" className="pm-donut-total-label">
              total
            </text>
          </svg>
          <ul className="pm-legend">
            {items.map((item, i) => (
              <li key={item.label}>
                <button type="button" className="pm-legend-btn" onClick={() => onSelect?.(item.label)}>
                  <span className="pm-swatch" style={{ background: item.fill ?? seriesColor(i, item.label) }} />
                  <span>{item.label}</span>
                  <strong>{formatNumber(item.value)}</strong>
                </button>
              </li>
            ))}
          </ul>
        </div>
      }
    </section>
  );
}

interface LineChartProps {
  title: string;
  items: BarChartItem[];
  ariaLabel: string;
  valueMode?: 'number' | 'usd';
  onSelect?: (item: BarChartItem) => void;
}

export function DashboardLineChart({ title, items, ariaLabel, valueMode = 'number', onSelect }: LineChartProps) {
  const w = 640;
  const h = 180;
  const pad = { l: 44, r: 12, t: 16, b: 36 };
  const max = items.reduce((m, x) => Math.max(m, x.value), 0);
  const innerW = w - pad.l - pad.r;
  const innerH = h - pad.t - pad.b;
  const pts = items.map((item, i) => {
    const x = pad.l + (items.length <= 1 ? innerW / 2 : (i / (items.length - 1)) * innerW);
    const y = pad.t + innerH - (max > 0 ? (item.value / max) * innerH : 0);
    return { x, y, item };
  });
  const d = pts.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x} ${p.y}`).join(' ');

  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <svg viewBox={`0 0 ${w} ${h}`} className="pm-line-svg" role="img" aria-label={ariaLabel}>
          <line x1={pad.l} y1={pad.t} x2={pad.l} y2={h - pad.b} stroke="#223550" />
          <line x1={pad.l} y1={h - pad.b} x2={w - pad.r} y2={h - pad.b} stroke="#223550" />
          {d ? <path d={d} fill="none" stroke="#20d4e8" strokeWidth="2.2" /> : null}
          {pts.map((p) => (
            <g key={p.item.label}>
              <circle
                cx={p.x}
                cy={p.y}
                r="4.5"
                fill={p.item.fill ?? '#f08700'}
                className={onSelect ? 'pm-chart-hit' : undefined}
                onClick={() => onSelect?.(p.item)}
              />
              <text x={p.x} y={h - 12} textAnchor="middle" className="pm-axis-label">
                {p.item.label}
              </text>
            </g>
          ))}
          <text x="8" y={pad.t + 4} className="pm-axis-label">
            {valueMode === 'usd' ? formatUsd(max) : formatNumber(max)}
          </text>
        </svg>
      }
    </section>
  );
}

interface ScatterProps {
  title: string;
  points: { label: string; x: number; y: number }[];
  xLabel: string;
  yLabel: string;
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

export function DashboardScatterChart({ title, points, xLabel, yLabel, ariaLabel, onSelect }: ScatterProps) {
  const w = 640;
  const h = 220;
  const pad = { l: 52, r: 16, t: 16, b: 40 };
  const maxX = points.reduce((m, p) => Math.max(m, p.x), 0);
  const maxY = points.reduce((m, p) => Math.max(m, p.y), 0);
  const innerW = w - pad.l - pad.r;
  const innerH = h - pad.t - pad.b;

  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {points.length === 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <svg viewBox={`0 0 ${w} ${h}`} className="pm-line-svg" role="img" aria-label={ariaLabel}>
          <line x1={pad.l} y1={pad.t} x2={pad.l} y2={h - pad.b} stroke="#223550" />
          <line x1={pad.l} y1={h - pad.b} x2={w - pad.r} y2={h - pad.b} stroke="#223550" />
          {points.map((p, i) => {
            const x = pad.l + (maxX > 0 ? (p.x / maxX) * innerW : 0);
            const y = pad.t + innerH - (maxY > 0 ? (p.y / maxY) * innerH : 0);
            return (
              <g key={p.label}>
                <circle
                  cx={x}
                  cy={y}
                  r="7"
                  fill={seriesColor(i, p.label)}
                  opacity="0.85"
                  className={onSelect ? 'pm-chart-hit' : undefined}
                  onClick={() => onSelect?.(p.label)}
                >
                  <title>{`${p.label}: ${formatNumber(p.x, 1)} hrs / ${formatUsd(p.y)}`}</title>
                </circle>
              </g>
            );
          })}
          <text x={w / 2} y={h - 8} textAnchor="middle" className="pm-axis-label">
            {xLabel}
          </text>
          <text x="12" y="14" className="pm-axis-label">
            {yLabel}
          </text>
        </svg>
      }
    </section>
  );
}

interface ParetoProps {
  title: string;
  items: { label: string; value: number }[];
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

export function DashboardParetoChart({ title, items, ariaLabel, onSelect }: ParetoProps) {
  const total = items.reduce((s, x) => s + x.value, 0);
  let running = 0;
  const w = 640;
  const h = 200;
  const pad = { l: 8, r: 8, t: 16, b: 48 };
  const innerW = w - pad.l - pad.r;
  const innerH = h - pad.t - pad.b;
  const max = items.reduce((m, x) => Math.max(m, x.value), 0);
  const barW = items.length > 0 ? innerW / items.length : innerW;
  const pts = items.map((item, i) => {
    running += item.value;
    const cum = total > 0 ? running / total : 0;
    const x = pad.l + i * barW + barW / 2;
    const y = pad.t + innerH - cum * innerH;
    return { x, y, item, cum, i };
  });
  const line = pts.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x} ${p.y}`).join(' ');

  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <>
          <svg viewBox={`0 0 ${w} ${h}`} className="pm-line-svg" role="img" aria-label={ariaLabel}>
            {pts.map((p) => {
              const bh = max > 0 ? (p.item.value / max) * innerH : 0;
              return (
                <rect
                  key={p.item.label}
                  x={pad.l + p.i * barW + 2}
                  y={pad.t + innerH - bh}
                  width={Math.max(4, barW - 4)}
                  height={bh}
                  fill="#20d4e8"
                  className={onSelect ? 'pm-chart-hit' : undefined}
                  onClick={() => onSelect?.(p.item.label)}
                />
              );
            })}
            {line ? <path d={line} fill="none" stroke="#f08700" strokeWidth="2" /> : null}
          </svg>
          <p className="field-help pm-pareto-caption">
            Bars = LatestRevenue. Line = cumulative share
            {total > 0 ? ` (100% = ${formatUsd(total)})` : ''}.
          </p>
        </>
      }
    </section>
  );
}

interface TreemapProps {
  title: string;
  items: { label: string; value: number; fill?: string }[];
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

export function DashboardTreemap({ title, items, ariaLabel, onSelect }: TreemapProps) {
  const total = items.reduce((s, x) => s + Math.max(0, x.value), 0);
  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 || total <= 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <div className="pm-treemap">
          {items.map((item, i) => (
            <button
              type="button"
              key={item.label}
              className="pm-treemap-cell"
              style={{
                flexGrow: Math.max(item.value, 1),
                background: item.fill ?? seriesColor(i, item.label),
              }}
              onClick={() => onSelect?.(item.label)}
            >
              <span>{item.label}</span>
              <strong>{formatUsd(item.value)}</strong>
            </button>
          ))}
        </div>
      }
    </section>
  );
}

interface ComboProps {
  title: string;
  items: { label: string; count: number; hours: number }[];
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

export function DashboardComboBar({ title, items, ariaLabel, onSelect }: ComboProps) {
  const maxCount = items.reduce((m, x) => Math.max(m, x.count), 0);
  const maxHours = items.reduce((m, x) => Math.max(m, x.hours), 0);
  return (
    <section className="dashboard-chart-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <div className="bar-chart">
          {items.map((item) => (
            <button
              type="button"
              key={item.label}
              className="bar-row bar-row--button pm-combo-row"
              onClick={() => onSelect?.(item.label)}
            >
              <span className="bar-label">{item.label}</span>
              <div className="pm-combo-tracks">
                <div className="bar-track">
                  <div
                    className="bar-fill"
                    style={{
                      width: `${maxCount > 0 ? (item.count / maxCount) * 100 : 0}%`,
                      background: '#20d4e8',
                    }}
                  />
                </div>
                <div className="bar-track">
                  <div
                    className="bar-fill"
                    style={{
                      width: `${maxHours > 0 ? (item.hours / maxHours) * 100 : 0}%`,
                      background: '#f08700',
                    }}
                  />
                </div>
              </div>
              <span className="bar-value">
                {formatNumber(item.count)} / {formatNumber(item.hours, 1)}h
              </span>
            </button>
          ))}
          <p className="field-help">Cyan = project count. Orange = TotalHours.</p>
        </div>
      }
    </section>
  );
}

interface HeatCell {
  rowLabel: string;
  colLabel: string;
  active: boolean;
  rowKey: string;
}

interface HeatmapProps {
  title: string;
  rowLabels: string[];
  colLabels: string[];
  cells: HeatCell[];
  ariaLabel: string;
  onSelectRow?: (rowKey: string) => void;
}

export function DashboardHeatmap({ title, rowLabels, colLabels, cells, ariaLabel, onSelectRow }: HeatmapProps) {
  const lookup = new Map(cells.map((c) => [`${c.rowKey}||${c.colLabel}`, c]));
  const shownCols = colLabels.slice(0, 18);
  return (
    <section className="dashboard-chart-card pm-heatmap-card" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {rowLabels.length === 0 || shownCols.length === 0 ?
        <p className="field-help" role="status">No service columns to display.</p>
      : <div className="pm-heatmap-wrap">
          <table className="pm-heatmap">
            <thead>
              <tr>
                <th>Client / project</th>
                {shownCols.map((c) => (
                  <th key={c} title={c}>
                    {c.replace(/_/g, ' ')}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rowLabels.slice(0, 40).map((rowLabel) => {
                const first = cells.find((c) => c.rowLabel === rowLabel);
                const key = first?.rowKey ?? rowLabel;
                return (
                  <tr key={key}>
                    <th>
                      <button type="button" className="pm-heatmap-row-btn" onClick={() => onSelectRow?.(key)}>
                        {rowLabel}
                      </button>
                    </th>
                    {shownCols.map((col) => {
                      const cell = lookup.get(`${key}||${col}`);
                      return (
                        <td
                          key={col}
                          className={cell?.active ? 'pm-heat-on' : 'pm-heat-off'}
                          title={`${rowLabel} / ${col}: ${cell?.active ? 'active' : 'inactive'}`}
                        />
                      );
                    })}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      }
    </section>
  );
}
