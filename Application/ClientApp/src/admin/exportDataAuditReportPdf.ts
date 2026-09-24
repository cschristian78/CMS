export type DataAuditExportCheckSummary = {
  checkId: string;
  title: string;
  description: string;
  count: number;
};

export type DataAuditExportSection = {
  key: string;
  label: string;
  findingTotal: number;
  checks: DataAuditExportCheckSummary[];
};

export type DataAuditExportFinding = {
  sectionKey: string;
  sectionLabel: string;
  checkId: string;
  checkTitle: string;
  entityLabel: string;
};

export type DataAuditExportLeadGroup = {
  analyticsLeadUserID?: number | null;
  analyticsLeadName: string;
  analyticsLeadEmail?: string | null;
  findingTotal: number;
  findings: DataAuditExportFinding[];
};

export type DataAuditExportReport = {
  generatedAtUtc: string;
  sections: DataAuditExportSection[];
  byAnalyticsLead: DataAuditExportLeadGroup[];
};

function blank(value: string | null | undefined, fallback = '—'): string {
  const t = (value ?? '').trim();
  return t.length > 0 ? t : fallback;
}

function formatStamp(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) {
    return new Date().toLocaleString();
  }
  return d.toLocaleString();
}

function fileStamp(iso: string): string {
  const d = new Date(iso);
  const use = Number.isNaN(d.getTime()) ? new Date() : d;
  const y = use.getFullYear();
  const m = String(use.getMonth() + 1).padStart(2, '0');
  const day = String(use.getDate()).padStart(2, '0');
  return `${y}${m}${day}`;
}

export function dataAuditExportFileName(report: DataAuditExportReport): string {
  return `DataAuditReport_${fileStamp(report.generatedAtUtc)}.pdf`;
}

/** Build a downloadable PDF summarizing Data Audit sections and Analytics Lead rollups. */
export async function createDataAuditReportPdfBlob(report: DataAuditExportReport): Promise<Blob> {
  const { jsPDF } = await import('jspdf');
  const autoTable = (await import('jspdf-autotable')).default;

  const doc = new jsPDF({ orientation: 'portrait', unit: 'pt', format: 'letter' });
  const pageWidth = doc.internal.pageSize.getWidth();
  const marginX = 40;
  /** Approximate height of two blank text lines at body size. */
  const blankLines = 24;
  let y = 40;

  const ensureSpace = (needed: number) => {
    const pageHeight = doc.internal.pageSize.getHeight();
    if (y + needed > pageHeight - 40) {
      doc.addPage();
      y = 40;
    }
  };

  const spaceAboveBelow = () => {
    ensureSpace(blankLines + 8);
    y += blankLines;
  };

  const sectionTitle = (title: string) => {
    ensureSpace(28 + blankLines);
    y += blankLines;
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

  const bodyText = (text: string, size = 9) => {
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(size);
    doc.setTextColor(74, 90, 106);
    const lines = doc.splitTextToSize(text, pageWidth - marginX * 2) as string[];
    ensureSpace(lines.length * 12 + 4);
    doc.text(lines, marginX, y);
    y += lines.length * 12 + 6;
    doc.setTextColor(26, 35, 50);
  };

  const dataTable = (head: string[], body: string[][], empty: string) => {
    autoTable(doc, {
      startY: y,
      margin: { left: marginX, right: marginX },
      theme: 'grid',
      head: [head],
      body: body.length > 0 ? body : [[empty, ...head.slice(1).map(() => '')]],
      styles: { fontSize: 8.5, cellPadding: 3, overflow: 'linebreak', textColor: [26, 35, 50] },
      headStyles: { fillColor: [238, 243, 248], textColor: [11, 61, 92], fontStyle: 'bold' },
    });
    y = ((doc as unknown as { lastAutoTable?: { finalY: number } }).lastAutoTable?.finalY ?? y) + 14;
  };

  const linkageFindingTotal = (report.sections ?? [])
    .filter((s) => s.key !== 'logins')
    .reduce((sum, s) => sum + (s.findingTotal ?? 0), 0);

  doc.setFont('helvetica', 'bold');
  doc.setFontSize(16);
  doc.setTextColor(11, 61, 92);
  doc.text('Data Audit Report', marginX, y);
  y += 16;
  doc.setFont('helvetica', 'normal');
  doc.setFontSize(10);
  doc.setTextColor(74, 90, 106);
  doc.text(`Generated ${formatStamp(report.generatedAtUtc)}`, marginX, y);
  y += 12;
  doc.setFontSize(9);
  doc.text(
    `${linkageFindingTotal} linkage finding${linkageFindingTotal === 1 ? '' : 's'} across Clients, Projects, and User groups.`,
    marginX,
    y,
  );
  y += 8;
  doc.setDrawColor(11, 61, 92);
  doc.setLineWidth(1.5);
  doc.line(marginX, y, pageWidth - marginX, y);
  y += 16;
  doc.setTextColor(26, 35, 50);

  sectionTitle('Section summaries');
  bodyText(
    'Counts below match the Data Audit tiles. Logins shows total CAT sign-ins (activity), not a linkage gap count.',
  );

  for (const section of report.sections ?? []) {
    spaceAboveBelow();
    ensureSpace(24);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(11);
    doc.setTextColor(11, 61, 92);
    const totalNote =
      section.key === 'logins'
        ? `${section.findingTotal} total login${section.findingTotal === 1 ? '' : 's'}`
        : `${section.findingTotal} finding${section.findingTotal === 1 ? '' : 's'}`;
    doc.text(`${section.label} — ${totalNote}`, marginX, y);
    y += 10;
    doc.setTextColor(26, 35, 50);

    const rows = (section.checks ?? []).map((c) => [
      blank(c.title),
      String(c.count ?? 0),
      blank(c.description),
    ]);
    dataTable(['Check', 'Count', 'Description'], rows, 'No checks in this section.');
    spaceAboveBelow();
  }

  sectionTitle('Issues by Analytics Lead');
  bodyText(
    'Findings are attributed to the client Analytics Lead (client/project issues) or the user-group Analytics Lead member when assigned. Unassigned means no Analytics Lead was set on the related client or group. Login activity is not included here.',
  );

  const leadGroups = report.byAnalyticsLead ?? [];
  if (leadGroups.length === 0) {
    spaceAboveBelow();
    bodyText('No attributed linkage findings.');
    spaceAboveBelow();
  } else {
    for (const lead of leadGroups) {
      const name = blank(lead.analyticsLeadName, 'Unassigned');
      const email = (lead.analyticsLeadEmail ?? '').trim();
      const heading = email ? `${name} (${email})` : name;
      spaceAboveBelow();
      ensureSpace(24);
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(11);
      doc.setTextColor(11, 61, 92);
      doc.text(
        `${heading} — ${lead.findingTotal} finding${lead.findingTotal === 1 ? '' : 's'}`,
        marginX,
        y,
      );
      y += 10;
      doc.setTextColor(26, 35, 50);

      const rows = (lead.findings ?? []).map((f) => [
        blank(f.sectionLabel),
        blank(f.checkTitle),
        blank(f.entityLabel),
      ]);
      dataTable(['Section', 'Check', 'Item'], rows, 'No findings for this lead.');
      spaceAboveBelow();
    }
  }

  ensureSpace(36 + blankLines);
  y += blankLines;
  doc.setFont('helvetica', 'italic');
  doc.setFontSize(8);
  doc.setTextColor(74, 90, 106);
  doc.text(
    'Unassigned = no Analytics Lead on the related client (for client/project findings) or user group (for group findings).',
    marginX,
    y,
  );

  return doc.output('blob');
}

export async function downloadDataAuditReportPdf(report: DataAuditExportReport): Promise<string> {
  const blob = await createDataAuditReportPdfBlob(report);
  const fileName = dataAuditExportFileName(report);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1500);
  return fileName;
}
