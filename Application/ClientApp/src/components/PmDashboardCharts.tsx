import { Fragment } from 'react';
import type { BarChartItem } from './DashboardBarChart';
import { formatNumber, formatPercent, formatUsd, formatUsdThousands, seriesColor } from '../dashboard/dashboardColors';
import type { NamedMetric, RenewalTimelinePeriod, TimelineGrain } from '../dashboard/dashboardData';

const TIMELINE_REVENUE_FILL = '#20d4e8';
const TIMELINE_COUNT_FILL = '#f08700';

function scaleBarPx(value: number, max: number, plotH: number): number {
  if (!Number.isFinite(value) || value <= 0) {
    return 0;
  }
  if (max <= 0) {
    return 12;
  }
  return Math.max(8, Math.round((value / max) * plotH));
}

interface SliceItem {
  label: string;
  value: number;
  revenue?: number;
  fill?: string;
}

interface DonutChartProps {
  title: string;
  items: SliceItem[];
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

// ---------------------------------------------------------------------------
// LeadTableExhibit – shows all account executive names with totals.
// ---------------------------------------------------------------------------
interface LeadTableExhibitProps {
  title: string;
  items: NamedMetric[];       // pre-sorted, full list (no slicing done outside)
  ariaLabel: string;
  onSelect?: (label: string) => void;
}

export function LeadTableExhibit({ title, items, ariaLabel, onSelect }: LeadTableExhibitProps) {
  const totalCount   = items.reduce((s, m) => s + m.count, 0);
  const totalRevenue = items.reduce((s, m) => s + m.revenue, 0);

  return (
    <section className="dashboard-chart-card pm-lead-table-exhibit" aria-label={ariaLabel}>
      <h2 className="dashboard-chart-title">{title}</h2>
      {items.length === 0 || totalCount === 0 ?
        <p className="field-help" role="status">No data to display.</p>
      : <div className="pm-lead-table-wrap">
          <table className="pm-lead-table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col" className="num">Projects</th>
                <th scope="col" className="num">Revenue</th>
              </tr>
            </thead>
            <tbody>
              {items.map((m, i) => (
                <tr key={m.label} className={onSelect ? 'pm-lead-table-row--clickable' : undefined}>
                  <td>
                    <button
                      type="button"
                      className="pm-lead-table-btn"
                      onClick={() => onSelect?.(m.label)}
                    >
                      <span className="pm-swatch" style={{ background: seriesColor(i, m.label) }} />
                      {m.label}
                    </button>
                  </td>
                  <td className="num">{formatNumber(m.count)}</td>
                  <td className="num">{formatUsd(m.revenue)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="pm-lead-table-total">
                <td>Total</td>
                <td className="num">{formatNumber(totalCount)}</td>
                <td className="num">{formatUsd(totalRevenue)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      }
    </section>
  );
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
  const totalRevenue = items.reduce((s, x) => s + Math.max(0, x.revenue ?? 0), 0);
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
          <p className="pm-exhibit-totals">
            Total · {formatNumber(total)} projects · {formatUsd(totalRevenue)}
          </p>
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

interface RevenueRenewalTimelineProps {
  title: string;
  ariaLabel: string;
  grain: TimelineGrain;
  periods: RenewalTimelinePeriod[];
  selectedKey: string | null;
  /** Bar plot height in px — matches Top Clients by Revenue when 280. */
  chartHeight?: number;
  wideChart?: boolean;
  className?: string;
  onGrainChange: (grain: TimelineGrain) => void;
  onSelect: (period: RenewalTimelinePeriod) => void;
}

export function DashboardRevenueRenewalTimeline({
  title,
  ariaLabel,
  grain,
  periods,
  selectedKey,
  chartHeight = 280,
  wideChart = false,
  className,
  onGrainChange,
  onSelect,
}: RevenueRenewalTimelineProps) {
  const selected = periods.find((p) => p.key === selectedKey) ?? null;
  const totalRenewalCount = periods.reduce((sum, period) => sum + period.renewalCount, 0);
  const totalRevenue = periods.reduce((sum, period) => sum + period.revenue, 0);
  const totalOnlineCount = periods.reduce((sum, period) => sum + period.onlineRenewalCount, 0);
  const totalOnlineRevenue = periods.reduce((sum, period) => sum + period.onlineRevenue, 0);
  const chartPeriods = periods.filter((period) => period.renewalCount > 0 || period.revenue > 0);
  const visiblePeriods = chartPeriods.length > 0 ? chartPeriods : periods;
  const plotH = chartHeight;
  const maxRevenue = visiblePeriods.reduce((m, p) => Math.max(m, p.revenue), 0);
  const maxRenewals = visiblePeriods.reduce((m, p) => Math.max(m, p.renewalCount), 0);
  const summaryRows: [string, string][] = selected
    ? [
        ['Period', selected.label],
        ['Projects', formatNumber(selected.renewalCount)],
        ['In Progress projects', formatNumber(selected.onlineRenewalCount)],
        ['% of projects', formatPercent(selected.renewalCount, totalRenewalCount)],
        ['Total revenue', formatUsd(selected.revenue)],
        ['In Progress revenue', formatUsd(selected.onlineRevenue)],
        ['% of revenue', formatPercent(selected.revenue, totalRevenue)],
        ['Min revenue', formatUsd(selected.minRevenue)],
        ['Max revenue', formatUsd(selected.maxRevenue)],
        ['Average revenue', formatUsd(selected.avgRevenue)],
        [
          'In Progress share (projects)',
          formatPercent(selected.onlineRenewalCount, selected.renewalCount),
        ],
        [
          'In Progress share (revenue)',
          formatPercent(selected.onlineRevenue, selected.revenue),
        ],
      ]
    : [];

  const cardClass = [
    'dashboard-chart-card',
    'pm-timeline-card',
    'pm-dash-simple-bars',
    wideChart ? 'pm-timeline-card--wide' : '',
    className,
  ].filter(Boolean).join(' ');

  const grainToggle = (
    <div className="pm-timeline-toggle" role="group" aria-label="Timeline grain">
      <button
        type="button"
        className={'pm-timeline-toggle__btn' + (grain === 'month' ? ' is-active' : '')}
        aria-pressed={grain === 'month'}
        onClick={() => onGrainChange('month')}
      >
        Monthly
      </button>
      <button
        type="button"
        className={'pm-timeline-toggle__btn' + (grain === 'quarter' ? ' is-active' : '')}
        aria-pressed={grain === 'quarter'}
        onClick={() => onGrainChange('quarter')}
      >
        Quarterly
      </button>
    </div>
  );

  return (
    <section className={cardClass} aria-label={ariaLabel}>
      <div className="pm-timeline-head">
        <div className="pm-timeline-head-text">
          <h2 className="dashboard-chart-title">{title}</h2>
          {periods.length > 0 ?
            <p className="pm-chart-value-key">
              Cyan = Revenue ($000) · Yellow = Projects
            </p>
          : null}
        </div>
        {periods.length > 0 ? grainToggle : null}
      </div>

      {periods.length === 0 ?
        <p className="field-help" role="status">No renewal dates to display.</p>
      : <div className={'pm-timeline-body' + (wideChart ? ' pm-timeline-body--chart-wide' : '')}>
          <div className="pm-timeline-main">
            <div className="bar-chart bar-chart--vertical" role="img" aria-label={ariaLabel}>
              <div
                className="bar-chart-plot pm-timeline-plot"
                style={{ ['--bar-plot-height' as string]: `${plotH}px` }}
              >
                {visiblePeriods.map((period, index) => {
                  const isSelected = period.key === selectedKey;
                  const revH = scaleBarPx(period.revenue, maxRevenue, plotH);
                  const countH = scaleBarPx(period.renewalCount, maxRenewals, plotH);
                  const revShown = formatUsdThousands(period.revenue);
                  const countShown = formatNumber(period.renewalCount);
                  return (
                    <Fragment key={period.key}>
                      {index > 0 ?
                        <div className="pm-timeline-set-gap" aria-hidden />
                      : null}
                      <button
                        type="button"
                        className={
                          'pm-timeline-set pm-timeline-period bar-column--button' +
                          (isSelected ? ' is-selected' : '')
                        }
                        onClick={() => onSelect(period)}
                        title={
                          `${period.label}: ${formatUsd(period.revenue)} revenue, ` +
                          `${formatNumber(period.renewalCount)} projects`
                        }
                      >
                        <div className="pm-timeline-period-pair" aria-hidden>
                          <div className="pm-timeline-series">
                            <span className="bar-column-value" title={revShown}>
                              {revShown || '\u00a0'}
                            </span>
                            <div className="bar-column-track pm-timeline-series-track">
                              {(period.revenue > 0 || period.renewalCount > 0) ?
                                <div
                                  className="bar-column-fill"
                                  style={{
                                    height: `${Math.max(revH, period.revenue > 0 ? 8 : 4)}px`,
                                    background: TIMELINE_REVENUE_FILL,
                                  }}
                                />
                              : null}
                            </div>
                          </div>
                          <div className="pm-timeline-series">
                            <span className="bar-column-value" title={countShown}>
                              {period.renewalCount > 0 ? countShown : '\u00a0'}
                            </span>
                            <div className="bar-column-track pm-timeline-series-track">
                              {(period.revenue > 0 || period.renewalCount > 0) ?
                                <div
                                  className="bar-column-fill"
                                  style={{
                                    height: `${Math.max(countH, period.renewalCount > 0 ? 8 : 4)}px`,
                                    background: TIMELINE_COUNT_FILL,
                                  }}
                                />
                              : null}
                            </div>
                          </div>
                        </div>
                        <span className="bar-column-label" title={period.label}>
                          {period.label}
                        </span>
                      </button>
                    </Fragment>
                  );
                })}
              </div>
            </div>
          </div>
          <p className="pm-exhibit-totals">
            Total · {formatNumber(totalRenewalCount)} projects · {formatUsd(totalRevenue)}
          </p>

          {selected ?
            <aside className="pm-timeline-side" aria-label="Timeline period summary">
              <table className="pm-timeline-summary" role="status" aria-live="polite">
                <caption className="visually-hidden">Selected period summary</caption>
                <tbody>
                  {summaryRows.map(([label, value]) => (
                    <tr key={label}>
                      <th scope="row">{label}</th>
                      <td>{value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="field-help pm-timeline-online-totals">
                Across timeline: {formatNumber(totalOnlineCount)} in progress projects ·{' '}
                {formatUsdThousands(totalOnlineRevenue)} in progress revenue ($000)
              </p>
            </aside>
          : null}
        </div>
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
