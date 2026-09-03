import {
  blankLabel,
  cellNumber,
  cellText,
  cellYmd,
  groupMetrics,
  sortTiers,
  summarizePortfolio,
  withOtherRemainder,
  type DashCell,
  type DashColIndex,
  type GlobalDashFilters,
} from './dashboardData';
import { formatNumber, formatUsd } from './dashboardColors';
import { formatCalendarDateDisplay } from '../utils/dateInput';

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function metricRowsHtml(
  title: string,
  items: Array<{ label: string; count: number; revenue?: number }>,
  includeRevenue = false,
): string {
  const body = items
    .map(
      (m) => `
      <tr>
        <td>${escapeHtml(m.label)}</td>
        <td class="num">${formatNumber(m.count)}</td>
        ${includeRevenue ? `<td class="num">${escapeHtml(formatUsd(m.revenue ?? 0))}</td>` : ''}
      </tr>`,
    )
    .join('');
  const totalCount = items.reduce((s, m) => s + m.count, 0);
  const totalRevenue = items.reduce((s, m) => s + (m.revenue ?? 0), 0);
  return `
    <section class="block">
      <h2>${escapeHtml(title)}</h2>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th class="num">Projects</th>
            ${includeRevenue ? '<th class="num">Latest Revenue</th>' : ''}
          </tr>
        </thead>
        <tbody>${body || '<tr><td colspan="3">No data</td></tr>'}</tbody>
        <tfoot>
          <tr class="total">
            <td>Total</td>
            <td class="num">${formatNumber(totalCount)}</td>
            ${includeRevenue ? `<td class="num">${escapeHtml(formatUsd(totalRevenue))}</td>` : ''}
          </tr>
        </tfoot>
      </table>
    </section>`;
}

function projectTableHtml(rows: DashCell[][], col: DashColIndex): string {
  const limited = rows.slice(0, 500);
  const body = limited
    .map((row) => {
      const status = blankLabel(cellText(row, col.projectStatus), 'Active');
      const tier = blankLabel(cellText(row, col.revenueTier), 'Unknown');
      const renewal = formatCalendarDateDisplay(cellYmd(row, col.renewalDate)) || '—';
      return `
      <tr>
        <td>${escapeHtml(cellText(row, col.clientName))}</td>
        <td>${escapeHtml(cellText(row, col.projectNumber))}</td>
        <td>${escapeHtml(cellText(row, col.projectName))}</td>
        <td>${escapeHtml(cellText(row, col.ptDescription))}</td>
        <td>${escapeHtml(status)}</td>
        <td>${escapeHtml(tier)}</td>
        <td class="num">${escapeHtml(formatUsd(cellNumber(row, col.latestRevenue)))}</td>
        <td>${escapeHtml(renewal)}</td>
        <td>${escapeHtml(blankLabel(cellText(row, col.accountExecutive), '—'))}</td>
        <td>${escapeHtml(blankLabel(cellText(row, col.analyticsLead), '—'))}</td>
      </tr>`;
    })
    .join('');

  const hidden = rows.slice(limited.length);
  const hiddenKpis = hidden.length > 0 ? summarizePortfolio(hidden, col) : null;
  const allKpis = summarizePortfolio(rows, col);
  const note =
    hidden.length > 0
      ? `<p class="note">Showing first ${limited.length} of ${rows.length} projects. Remaining projects are grouped as Other.</p>`
      : '';

  return `
    <section class="block">
      <h2>All Filtered Projects</h2>
      <table class="projects">
        <thead>
          <tr>
            <th>Client</th>
            <th>Project Number</th>
            <th>Project Name</th>
            <th>Type</th>
            <th>Status</th>
            <th>Tier</th>
            <th class="num">Latest Revenue</th>
            <th>Renewal Date</th>
            <th>Account Executive</th>
            <th>Analytics Lead</th>
          </tr>
        </thead>
        <tbody>${body || '<tr><td colspan="10">No projects match the current filters.</td></tr>'}</tbody>
        <tfoot>
          ${hiddenKpis ? `<tr><td colspan="6">Other (${formatNumber(hiddenKpis.projects)} projects not shown)</td><td class="num">${escapeHtml(formatUsd(hiddenKpis.revenue))}</td><td></td><td></td><td></td></tr>` : ''}
          <tr class="total"><td colspan="6">Total</td><td class="num">${escapeHtml(formatUsd(allKpis.revenue))}</td><td></td><td></td><td></td></tr>
        </tfoot>
      </table>
      ${note}
    </section>`;
}

function filterSummary(filters: GlobalDashFilters): string {
  const bits = [
    filters.businessUnit ? `Business Unit: ${filters.businessUnit}` : null,
    filters.businessClass ? `Business Class: ${filters.businessClass}` : null,
    filters.projectStatus ? `Project Status: ${filters.projectStatus}` : null,
    filters.statusDescription ? `Client Status: ${filters.statusDescription}` : null,
    filters.dateFrom || filters.dateTo
      ? `Date: ${filters.dateFrom || '…'} → ${filters.dateTo || '…'}`
      : null,
  ].filter(Boolean);
  return bits.length > 0 ? bits.join('  ·  ') : 'All filters: none (full filtered set)';
}

/** Opens a print-ready professional report for the Executive Overview tab (Save as PDF). */
export function exportExecutiveOverviewPdf(options: {
  rows: DashCell[][];
  col: DashColIndex;
  filters: GlobalDashFilters;
  detailTitle?: string;
}): void {
  const { rows, col, filters } = options;
  const kpis = summarizePortfolio(rows, col);
  const byTier = sortTiers(groupMetrics(rows, col, col.revenueTier, 'Unknown'));
  const byAe = withOtherRemainder(
    groupMetrics(rows, col, col.accountExecutive, '(No account executive)').sort(
      (a, b) => b.count - a.count || a.label.localeCompare(b.label),
    ),
    8,
  );
  const byAl = withOtherRemainder(
    groupMetrics(rows, col, col.analyticsLead, '(No analytics lead)').sort(
      (a, b) => b.count - a.count || a.label.localeCompare(b.label),
    ),
    8,
  );
  const topClients = withOtherRemainder(groupMetrics(rows, col, col.clientName, '(No client)'), 10);
  const generated = new Date().toLocaleString();

  const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Executive Overview Report</title>
  <style>
    @page { size: letter landscape; margin: 0.55in; }
    * { box-sizing: border-box; }
    body {
      font-family: "Segoe UI", Calibri, Arial, sans-serif;
      color: #1a2332;
      margin: 0;
      font-size: 10.5pt;
      line-height: 1.35;
    }
    header {
      border-bottom: 2.5px solid #0b3d5c;
      padding-bottom: 0.55rem;
      margin-bottom: 1rem;
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: 1rem;
    }
    header h1 {
      margin: 0;
      font-size: 1.55rem;
      font-weight: 650;
      color: #0b3d5c;
      letter-spacing: 0.01em;
    }
    header .meta {
      text-align: right;
      color: #4a5a6a;
      font-size: 0.85rem;
    }
    .filters {
      background: #f3f6f9;
      border: 1px solid #d5dde6;
      border-radius: 4px;
      padding: 0.45rem 0.65rem;
      margin-bottom: 1rem;
      color: #334;
      font-size: 0.88rem;
    }
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 0.65rem;
      margin-bottom: 1.1rem;
    }
    .kpi {
      border: 1px solid #cfd8e3;
      border-radius: 6px;
      padding: 0.65rem 0.8rem;
      background: linear-gradient(180deg, #ffffff 0%, #f7fafc 100%);
    }
    .kpi span {
      display: block;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #5a6b7c;
      margin-bottom: 0.2rem;
    }
    .kpi strong {
      font-size: 1.35rem;
      color: #0b3d5c;
      font-weight: 650;
    }
    .grid-2 {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0.85rem;
      margin-bottom: 0.85rem;
    }
    .block {
      break-inside: avoid;
      margin-bottom: 0.95rem;
    }
    h2 {
      margin: 0 0 0.4rem;
      font-size: 0.98rem;
      color: #0b3d5c;
      border-bottom: 1px solid #d5dde6;
      padding-bottom: 0.25rem;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.82rem;
    }
    th, td {
      border: 1px solid #d5dde6;
      padding: 0.28rem 0.4rem;
      text-align: left;
      vertical-align: top;
    }
    th {
      background: #0b3d5c;
      color: #fff;
      font-weight: 600;
    }
    tfoot tr.total td { background: #0b3d5c; color: #fff; font-weight: 650; }
    td.num, th.num { text-align: right; white-space: nowrap; }
    table.projects th, table.projects td { font-size: 0.76rem; }
    .note { color: #5a6b7c; font-size: 0.8rem; margin: 0.35rem 0 0; }
    footer {
      margin-top: 1.25rem;
      padding-top: 0.45rem;
      border-top: 1px solid #d5dde6;
      color: #5a6b7c;
      font-size: 0.75rem;
    }
    @media print {
      body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    }
  </style>
</head>
<body>
  <header>
    <div>
      <h1>Executive Overview</h1>
      <div>Client Management System · Dashboard report</div>
    </div>
    <div class="meta">
      <div>Generated ${escapeHtml(generated)}</div>
      <div>${escapeHtml(options.detailTitle ?? 'All filtered projects')}</div>
    </div>
  </header>

  <div class="filters">${escapeHtml(filterSummary(filters))}</div>

  <div class="kpi-row">
    <div class="kpi"><span>Projects</span><strong>${formatNumber(kpis.projects)}</strong></div>
    <div class="kpi"><span>Clients</span><strong>${formatNumber(kpis.clients)}</strong></div>
    <div class="kpi"><span>Latest Revenue</span><strong>${escapeHtml(formatUsd(kpis.revenue))}</strong></div>
  </div>

  <div class="grid-2">
    ${metricRowsHtml(
      'Revenue Tier Distribution',
      byTier.map((m) => ({ label: m.label, count: m.count, revenue: m.revenue })),
      true,
    )}
    ${metricRowsHtml(
      'Top Clients by Revenue',
      topClients.map((m) => ({ label: m.label, count: m.count, revenue: m.revenue })),
      true,
    )}
  </div>

  <div class="grid-2">
    ${metricRowsHtml(
      'Projects by Account Executive',
      byAe.map((m) => ({ label: m.label, count: m.count, revenue: m.revenue })),
      true,
    )}
    ${metricRowsHtml(
      'Projects by Analytics Lead',
      byAl.map((m) => ({ label: m.label, count: m.count, revenue: m.revenue })),
      true,
    )}
  </div>

  ${projectTableHtml(rows, col)}

  <footer>
    Confidential · For internal use · Project count in this report: ${rows.length}
  </footer>
</body>
</html>`;

  const iframeId = 'cms-executive-overview-print-frame';
  let iframe = document.getElementById(iframeId) as HTMLIFrameElement | null;
  if (iframe) {
    iframe.remove();
  }

  iframe = document.createElement('iframe');
  iframe.id = iframeId;
  iframe.title = 'Executive Overview print frame';
  iframe.setAttribute('aria-hidden', 'true');
  iframe.style.position = 'fixed';
  iframe.style.right = '0';
  iframe.style.bottom = '0';
  iframe.style.width = '0';
  iframe.style.height = '0';
  iframe.style.border = '0';
  iframe.style.opacity = '0';
  iframe.style.pointerEvents = 'none';
  document.body.appendChild(iframe);

  const frameWindow = iframe.contentWindow;
  const frameDocument = iframe.contentDocument ?? frameWindow?.document;
  if (!frameWindow || !frameDocument) {
    iframe.remove();
    throw new Error('Could not prepare the PDF report frame.');
  }

  const cleanup = () => {
    window.setTimeout(() => {
      iframe?.remove();
    }, 1000);
  };

  frameWindow.addEventListener('afterprint', cleanup);
  frameDocument.open();
  frameDocument.write(html);
  frameDocument.close();

  // Print after the frame document is ready (no pop-up window required).
  window.setTimeout(() => {
    try {
      frameWindow.focus();
      frameWindow.print();
    } catch {
      cleanup();
      throw new Error('Could not open the print dialog for the PDF report.');
    }
  }, 300);
}
