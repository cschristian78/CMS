import { formatCalendarDateDisplay, toInputDateValue } from '../utils/dateInput';

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function formatHours(n: number): string {
  if (!Number.isFinite(n) || n === 0) {
    return '0';
  }
  return String(Math.round(n * 100) / 100);
}

function blank(value: string | null | undefined, fallback = '—'): string {
  const t = (value ?? '').trim();
  return t.length > 0 ? t : fallback;
}

function dlRow(label: string, value: string): string {
  return `<tr><th>${escapeHtml(label)}</th><td>${escapeHtml(value)}</td></tr>`;
}

function simpleTable(
  title: string,
  headers: string[],
  rows: string[][],
  emptyMessage: string,
): string {
  const head = headers.map((h) => `<th>${escapeHtml(h)}</th>`).join('');
  const body =
    rows.length === 0
      ? `<tr><td colspan="${headers.length}">${escapeHtml(emptyMessage)}</td></tr>`
      : rows
          .map(
            (cells) =>
              `<tr>${cells.map((c, i) => `<td${i === cells.length - 1 && headers[i]?.toLowerCase().includes('hour') ? ' class="num"' : ''}>${escapeHtml(c)}</td>`).join('')}</tr>`,
          )
          .join('');
  return `
    <section class="block">
      <h2>${escapeHtml(title)}</h2>
      <table>
        <thead><tr>${head}</tr></thead>
        <tbody>${body}</tbody>
      </table>
    </section>`;
}

export type ProjectReportClient = {
  clientName: string;
  clientShortName?: string | null;
  businessUnit?: string | null;
  businessClass?: string | null;
  clientType?: string | null;
  clientStatus?: string | null;
};

export type ProjectReportProject = {
  projectId: number;
  projectNumber?: string | null;
  projectName: string;
  projectType?: string | null;
  /** Short project type code (e.g. from tProjectType.ProjectTypeCode). */
  projectTypeCode?: string | null;
  userGroup?: string | null;
  status?: string | null;
  isOnline?: boolean;
  renewalDate?: string | null;
  dueDate?: string | null;
  latestRevenue?: number | null;
};

export type ProjectReportUserGroupMember = {
  name: string;
  title: string;
  role: string;
  phone: string;
  email: string;
};

export type ProjectReportDataSource = {
  fileName: string;
  category: string;
  version: string;
  dataAsOf: string;
  active: boolean;
  uploadedBy: string;
};

export type ProjectReportService = {
  serviceName: string;
  category: string;
};

export type ProjectReportCalendarItem = {
  itemType: string;
  title: string;
  owner: string;
  receivedDate: string;
  completedDate: string;
  notes: string;
};

export type ProjectReportTimeRow = {
  label: string;
  detail?: string | null;
  hours: number;
};

export type ProjectReportNote = {
  noteType: string;
  noteDate: string;
  author: string;
  pinned: boolean;
  text: string;
};

export type ProjectReportInput = {
  client: ProjectReportClient;
  project: ProjectReportProject;
  userGroupMembers: ProjectReportUserGroupMember[];
  dataSources: ProjectReportDataSource[];
  services: ProjectReportService[];
  calendar: ProjectReportCalendarItem[];
  timeByAnalyst: ProjectReportTimeRow[];
  timeByTask: ProjectReportTimeRow[];
  totalHours: number;
  notes: ProjectReportNote[];
};

function fileNameSegment(value: string | null | undefined, fallback: string, maxLen = 80): string {
  const cleaned = (value ?? '')
    .trim()
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_')
    .replace(/\s+/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_|_$/g, '')
    .slice(0, maxLen);
  return cleaned || fallback;
}

function renewalDateMmddyyyy(renewalDate?: string | null): string {
  const iso = toInputDateValue(renewalDate);
  const m = iso.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) {
    return '00000000';
  }
  return `${m[2]}${m[3]}${m[1]}`;
}

/**
 * Default report file name:
 * ClientShortName_RenewalDate(MMDDYYYY)_ProjectTypeCode_ProjectName_ProjectReport.pdf
 */
export function suggestedProjectReportFileName(input: ProjectReportInput): string {
  const clientShort = fileNameSegment(input.client.clientShortName, 'Client', 40);
  const renewal = renewalDateMmddyyyy(input.project.renewalDate);
  const typeCode = fileNameSegment(input.project.projectTypeCode, 'Type', 30);
  const projectName = fileNameSegment(input.project.projectName, `Project${input.project.projectId}`, 80);
  return `${clientShort}_${renewal}_${typeCode}_${projectName}_ProjectReport.pdf`;
}

/** Sanitize a user-entered report file name and ensure a .pdf extension. */
export function sanitizeProjectReportFileName(
  value: string | null | undefined,
  fallback: string,
): string {
  const fallbackSafe = fallback.trim().toLowerCase().endsWith('.pdf')
    ? fallback.trim()
    : `${fallback.trim() || 'ProjectReport'}.pdf`;
  let name = (value ?? '').trim();
  if (!name) {
    return fallbackSafe;
  }
  name = name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').replace(/\s+/g, ' ').trim();
  name = name.replace(/\.+$/g, '');
  if (!name) {
    return fallbackSafe;
  }
  if (!name.toLowerCase().endsWith('.pdf')) {
    name = `${name}.pdf`;
  }
  return name.slice(0, 180);
}

/** Build a downloadable/uploadable PDF of the project report. */
export async function createProjectReportPdfBlob(input: ProjectReportInput): Promise<Blob> {
  const { jsPDF } = await import('jspdf');
  const autoTable = (await import('jspdf-autotable')).default;

  const doc = new jsPDF({ orientation: 'portrait', unit: 'pt', format: 'letter' });
  const pageWidth = doc.internal.pageSize.getWidth();
  const marginX = 40;
  let y = 40;

  const ensureSpace = (needed: number) => {
    const pageHeight = doc.internal.pageSize.getHeight();
    if (y + needed > pageHeight - 40) {
      doc.addPage();
      y = 40;
    }
  };

  const sectionTitle = (title: string) => {
    ensureSpace(28);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.setTextColor(11, 61, 92);
    doc.text(title, marginX, y);
    y += 6;
    doc.setDrawColor(213, 221, 230);
    doc.line(marginX, y, pageWidth - marginX, y);
    y += 10;
    doc.setTextColor(26, 35, 50);
  };

  const kvTable = (rows: Array<[string, string]>) => {
    autoTable(doc, {
      startY: y,
      margin: { left: marginX, right: marginX },
      theme: 'plain',
      styles: { fontSize: 9, cellPadding: 2.5, textColor: [26, 35, 50] },
      columnStyles: {
        0: { cellWidth: 120, fontStyle: 'bold', textColor: [74, 90, 106] },
        1: { cellWidth: 'auto' },
      },
      body: rows.map(([k, v]) => [k, blank(v)]),
    });
    y = ((doc as unknown as { lastAutoTable?: { finalY: number } }).lastAutoTable?.finalY ?? y) + 14;
  };

  const dataTable = (title: string, head: string[], body: string[][], empty: string) => {
    sectionTitle(title);
    autoTable(doc, {
      startY: y,
      margin: { left: marginX, right: marginX },
      theme: 'grid',
      head: [head],
      body: body.length > 0 ? body : [head.map((_, i) => (i === 0 ? empty : ''))],
      styles: { fontSize: 8.5, cellPadding: 3, overflow: 'linebreak', textColor: [26, 35, 50] },
      headStyles: { fillColor: [238, 243, 248], textColor: [11, 61, 92], fontStyle: 'bold' },
    });
    y = ((doc as unknown as { lastAutoTable?: { finalY: number } }).lastAutoTable?.finalY ?? y) + 14;
  };

  const projectTitle = blank(input.project.projectName, `Project ${input.project.projectId}`);
  const clientTitle = blank(input.client.clientName, 'Client');
  const generated = new Date().toLocaleString();

  doc.setFont('helvetica', 'bold');
  doc.setFontSize(16);
  doc.setTextColor(11, 61, 92);
  doc.text('Project Report', marginX, y);
  y += 16;
  doc.setFont('helvetica', 'normal');
  doc.setFontSize(10);
  doc.setTextColor(74, 90, 106);
  doc.text(`${clientTitle} · ${projectTitle}`, marginX, y);
  y += 12;
  doc.setFontSize(9);
  doc.text(`Generated ${generated}`, marginX, y);
  y += 8;
  doc.setDrawColor(11, 61, 92);
  doc.setLineWidth(1.5);
  doc.line(marginX, y, pageWidth - marginX, y);
  y += 16;
  doc.setTextColor(26, 35, 50);

  sectionTitle('Client');
  kvTable([
    ['Client name', blank(input.client.clientName)],
    ['Short name', blank(input.client.clientShortName)],
    ['Business unit', blank(input.client.businessUnit)],
    ['Business class', blank(input.client.businessClass)],
    ['Client type', blank(input.client.clientType)],
    ['Status', blank(input.client.clientStatus)],
  ]);

  sectionTitle('Project');
  kvTable([
    ['Project ID', String(input.project.projectId)],
    ['Project number', blank(input.project.projectNumber)],
    ['Project name', blank(input.project.projectName)],
    ['Project type', blank(input.project.projectType)],
    ['User group', blank(input.project.userGroup)],
    ['Status', blank(input.project.status)],
    ['Online', input.project.isOnline ? 'Online' : 'Offline'],
    [
      'Renewal date',
      blank(formatCalendarDateDisplay(input.project.renewalDate ?? undefined) || input.project.renewalDate),
    ],
    [
      'Due date',
      blank(formatCalendarDateDisplay(input.project.dueDate ?? undefined) || input.project.dueDate),
    ],
    [
      'Latest revenue',
      input.project.latestRevenue != null && Number.isFinite(input.project.latestRevenue)
        ? `$${Number(input.project.latestRevenue).toLocaleString(undefined, { maximumFractionDigits: 2 })}`
        : '—',
    ],
  ]);

  dataTable(
    `User group — ${blank(input.project.userGroup, 'User group')}`,
    ['Name', 'Title', 'Role', 'Phone', 'Email'],
    input.userGroupMembers.map((m) => [m.name, m.title, m.role, m.phone, m.email]),
    'No members in the selected user group.',
  );

  dataTable(
    'Data sources',
    ['File', 'Classification', 'Version', 'Data as of', 'Active', 'Uploaded by'],
    input.dataSources.map((f) => [
      f.fileName,
      f.category,
      f.version,
      f.dataAsOf,
      f.active ? 'Yes' : 'No',
      f.uploadedBy,
    ]),
    'No project data files.',
  );

  dataTable(
    'Selected services',
    ['Service', 'Category'],
    input.services.map((s) => [s.serviceName, s.category]),
    'No services selected.',
  );

  dataTable(
    'Analytics calendar',
    ['Type', 'Title', 'Owner', 'Received', 'Completed', 'Notes'],
    input.calendar.map((c) => [c.itemType, c.title, c.owner, c.receivedDate, c.completedDate, c.notes]),
    'No calendar items.',
  );

  dataTable(
    'Time tracking by analyst',
    ['Analyst', 'Hours'],
    input.timeByAnalyst.map((r) => [r.label, formatHours(r.hours)]),
    'No time entries.',
  );

  dataTable(
    'Time tracking by task',
    ['Task', 'Type', 'Hours'],
    input.timeByTask.map((r) => [r.label, blank(r.detail, '—'), formatHours(r.hours)]),
    'No time entries.',
  );

  ensureSpace(20);
  doc.setFont('helvetica', 'bold');
  doc.setFontSize(10);
  doc.text(`Total hours: ${formatHours(input.totalHours)}`, marginX, y);
  y += 18;

  sectionTitle('Project notes');
  if (input.notes.length === 0) {
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(9);
    doc.setTextColor(74, 90, 106);
    doc.text('No project notes.', marginX, y);
  } else {
    for (const n of input.notes) {
      const header = `${blank(n.noteType, 'General')}${n.pinned ? ' (Pinned)' : ''} · ${blank(n.noteDate)} · ${blank(n.author)}`;
      const text = blank(n.text, '(empty)');
      const lines = doc.splitTextToSize(text, pageWidth - marginX * 2);
      ensureSpace(28 + lines.length * 11);
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(9);
      doc.setTextColor(11, 61, 92);
      doc.text(header, marginX, y);
      y += 12;
      doc.setFont('helvetica', 'normal');
      doc.setTextColor(26, 35, 50);
      doc.text(lines, marginX, y);
      y += lines.length * 11 + 10;
    }
  }

  return doc.output('blob');
}

/** Opens a print-ready project report (Save as PDF), styled like the Executive Overview. */
export function exportProjectReportPdf(input: ProjectReportInput): void {
  const generated = new Date().toLocaleString();
  const projectTitle = blank(input.project.projectName, `Project ${input.project.projectId}`);
  const clientTitle = blank(input.client.clientName, 'Client');

  const clientBlock = `
    <section class="block">
      <h2>Client</h2>
      <table class="kv">
        <tbody>
          ${dlRow('Client name', blank(input.client.clientName))}
          ${dlRow('Short name', blank(input.client.clientShortName))}
          ${dlRow('Business unit', blank(input.client.businessUnit))}
          ${dlRow('Business class', blank(input.client.businessClass))}
          ${dlRow('Client type', blank(input.client.clientType))}
          ${dlRow('Status', blank(input.client.clientStatus))}
        </tbody>
      </table>
    </section>`;

  const projectBlock = `
    <section class="block">
      <h2>Project</h2>
      <table class="kv">
        <tbody>
          ${dlRow('Project ID', String(input.project.projectId))}
          ${dlRow('Project number', blank(input.project.projectNumber))}
          ${dlRow('Project name', blank(input.project.projectName))}
          ${dlRow('Project type', blank(input.project.projectType))}
          ${dlRow('User group', blank(input.project.userGroup))}
          ${dlRow('Status', blank(input.project.status))}
          ${dlRow('Online', input.project.isOnline ? 'Online' : 'Offline')}
          ${dlRow('Renewal date', blank(formatCalendarDateDisplay(input.project.renewalDate ?? undefined) || input.project.renewalDate))}
          ${dlRow('Due date', blank(formatCalendarDateDisplay(input.project.dueDate ?? undefined) || input.project.dueDate))}
          ${dlRow(
            'Latest revenue',
            input.project.latestRevenue != null && Number.isFinite(input.project.latestRevenue)
              ? `$${Number(input.project.latestRevenue).toLocaleString(undefined, { maximumFractionDigits: 2 })}`
              : '—',
          )}
        </tbody>
      </table>
    </section>`;

  const groupName = blank(input.project.userGroup, 'User group');
  const userGroupBlock = simpleTable(
    `User group — ${groupName}`,
    ['Name', 'Title', 'Role', 'Phone', 'Email'],
    input.userGroupMembers.map((m) => [m.name, m.title, m.role, m.phone, m.email]),
    'No members in the selected user group.',
  );

  const dataSourcesBlock = simpleTable(
    'Data sources',
    ['File', 'Classification', 'Version', 'Data as of', 'Active', 'Uploaded by'],
    input.dataSources.map((f) => [
      f.fileName,
      f.category,
      f.version,
      f.dataAsOf,
      f.active ? 'Yes' : 'No',
      f.uploadedBy,
    ]),
    'No project data files.',
  );

  const servicesBlock = simpleTable(
    'Selected services',
    ['Service', 'Category'],
    input.services.map((s) => [s.serviceName, s.category]),
    'No services selected.',
  );

  const calendarBlock = simpleTable(
    'Analytics calendar',
    ['Type', 'Title', 'Owner', 'Received', 'Completed', 'Notes'],
    input.calendar.map((c) => [
      c.itemType,
      c.title,
      c.owner,
      c.receivedDate,
      c.completedDate,
      c.notes,
    ]),
    'No calendar items.',
  );

  const timeAnalystBlock = simpleTable(
    'Time tracking by analyst',
    ['Analyst', 'Hours'],
    input.timeByAnalyst.map((r) => [r.label, formatHours(r.hours)]),
    'No time entries.',
  );

  const timeTaskBlock = simpleTable(
    'Time tracking by task',
    ['Task', 'Type', 'Hours'],
    input.timeByTask.map((r) => [r.label, blank(r.detail, '—'), formatHours(r.hours)]),
    'No time entries.',
  );

  const timeTotal = `
    <p class="note"><strong>Total hours:</strong> ${escapeHtml(formatHours(input.totalHours))}</p>`;

  const notesBody =
    input.notes.length === 0
      ? '<p class="empty">No project notes.</p>'
      : input.notes
          .map(
            (n) => `
      <article class="note-card">
        <header>
          <span class="note-type">${escapeHtml(blank(n.noteType, 'General'))}</span>
          ${n.pinned ? '<span class="pinned">Pinned</span>' : ''}
          <span class="note-meta">${escapeHtml(blank(n.noteDate))} · ${escapeHtml(blank(n.author))}</span>
        </header>
        <div class="note-text">${escapeHtml(blank(n.text, '(empty)'))}</div>
      </article>`,
          )
          .join('');

  const notesBlock = `
    <section class="block">
      <h2>Project notes</h2>
      ${notesBody}
    </section>`;

  const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Project Report — ${escapeHtml(projectTitle)}</title>
  <style>
    @page { size: letter portrait; margin: 0.6in; }
    * { box-sizing: border-box; }
    body {
      font-family: "Segoe UI", Calibri, Arial, sans-serif;
      color: #1a2332;
      margin: 0;
      font-size: 10.5pt;
      line-height: 1.35;
    }
    header.report-header {
      border-bottom: 2.5px solid #0b3d5c;
      padding-bottom: 0.55rem;
      margin-bottom: 1rem;
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: 1rem;
    }
    header.report-header h1 {
      margin: 0;
      font-size: 1.45rem;
      font-weight: 650;
      color: #0b3d5c;
    }
    header.report-header .subtitle {
      margin: 0.2rem 0 0;
      color: #4a5a6a;
      font-size: 0.95rem;
    }
    header.report-header .meta {
      text-align: right;
      color: #4a5a6a;
      font-size: 0.85rem;
    }
    .block { margin: 0 0 1.15rem; break-inside: avoid; }
    .block h2 {
      margin: 0 0 0.45rem;
      font-size: 1.05rem;
      color: #0b3d5c;
      border-bottom: 1px solid #d5dde6;
      padding-bottom: 0.2rem;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 9.5pt;
    }
    table.kv th {
      width: 28%;
      text-align: left;
      color: #4a5a6a;
      font-weight: 600;
      padding: 0.28rem 0.45rem 0.28rem 0;
      vertical-align: top;
    }
    table.kv td {
      padding: 0.28rem 0;
      vertical-align: top;
    }
    table:not(.kv) th,
    table:not(.kv) td {
      border: 1px solid #d5dde6;
      padding: 0.3rem 0.4rem;
      text-align: left;
      vertical-align: top;
    }
    table:not(.kv) thead th {
      background: #eef3f8;
      color: #0b3d5c;
      font-weight: 650;
    }
    td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
    .note { color: #4a5a6a; font-size: 0.9rem; margin: 0.35rem 0 0; }
    .empty { color: #4a5a6a; margin: 0; }
    .note-card {
      border: 1px solid #d5dde6;
      border-radius: 0.3rem;
      padding: 0.55rem 0.65rem;
      margin: 0 0 0.55rem;
      background: #f8fafc;
      break-inside: avoid;
    }
    .note-card header {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem 0.75rem;
      align-items: baseline;
      margin-bottom: 0.35rem;
    }
    .note-type { font-weight: 700; color: #0b3d5c; }
    .pinned {
      font-size: 0.75rem;
      font-weight: 700;
      color: #9a3412;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }
    .note-meta { color: #4a5a6a; font-size: 0.85rem; }
    .note-text { white-space: pre-wrap; }
    @media print {
      body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    }
  </style>
</head>
<body>
  <header class="report-header">
    <div>
      <h1>Project Report</h1>
      <p class="subtitle">${escapeHtml(clientTitle)} · ${escapeHtml(projectTitle)}</p>
    </div>
    <div class="meta">Generated ${escapeHtml(generated)}</div>
  </header>
  ${clientBlock}
  ${projectBlock}
  ${userGroupBlock}
  ${dataSourcesBlock}
  ${servicesBlock}
  ${calendarBlock}
  ${timeAnalystBlock}
  ${timeTaskBlock}
  ${timeTotal}
  ${notesBlock}
</body>
</html>`;

  const iframeId = 'cms-project-report-print-frame';
  let iframe = document.getElementById(iframeId) as HTMLIFrameElement | null;
  if (iframe) {
    iframe.remove();
  }

  iframe = document.createElement('iframe');
  iframe.id = iframeId;
  iframe.title = 'Project report print frame';
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
    throw new Error('Could not create the project report print frame.');
  }

  const cleanup = () => {
    try {
      iframe?.remove();
    } catch {
      /* ignore */
    }
  };

  frameWindow.addEventListener('afterprint', cleanup);
  frameDocument.open();
  frameDocument.write(html);
  frameDocument.close();

  window.setTimeout(() => {
    try {
      frameWindow.focus();
      frameWindow.print();
    } catch {
      cleanup();
      throw new Error('Could not open the print dialog for the project report.');
    }
  }, 250);
}
