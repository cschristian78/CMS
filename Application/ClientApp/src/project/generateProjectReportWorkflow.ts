import type { FlashFn } from '../App';
import { ApiError, apiDelete, apiGet, apiPostFormData, apiPostJson } from '../api';
import { formatBusinessClassDisplay } from '../utils/businessClassDisplay';
import { formatCalendarDateDisplay, toInputDateValue } from '../utils/dateInput';
import {
  createProjectReportPdfBlob,
  exportProjectReportPdf,
  sanitizeProjectReportFileName,
  suggestedProjectReportFileName,
  type ProjectReportInput,
} from './exportProjectReportPdf';
import { loadProjectCatCcEmails } from './loadProjectCatCcEmails';
import { buildOutlookHtmlFromLines, openOutlookDraftWithAttachment } from './openOutlookDraftWithAttachment';

export type ProjectReportRunMode = 'interactive' | 'saveAndSendAttachment';

type SelectOption = { value: string; text: string };

type ProjectFileRow = {
  projectFileID: number;
  originalFileName: string;
  downloadUrl?: string | null;
  blobUrl?: string | null;
  category?: string | null;
  versionLabel?: string | null;
  dataAsOf?: string | null;
  isCurrent?: boolean;
  uploadedByName?: string | null;
};

type GroupMember = {
  userID: number;
  userFullName?: string;
  title?: string;
  email?: string;
  phone?: string;
  userRoleName?: string;
};

function normalizeSelectOptions(raw: unknown): SelectOption[] {
  if (!Array.isArray(raw)) {
    return [];
  }
  return raw
    .map((item): SelectOption | null => {
      if (item === null || typeof item !== 'object') {
        return null;
      }
      const o = item as Record<string, unknown>;
      const value = o.value ?? o.Value;
      const text = o.text ?? o.Text;
      if (value === undefined || value === null || String(value).trim() === '') {
        return null;
      }
      return { value: String(value), text: String(text ?? value) };
    })
    .filter((x): x is SelectOption => x != null);
}

function optionTextForValue(
  options: SelectOption[] | undefined,
  value: number | '' | null | undefined,
): string {
  if (value === '' || value == null) {
    return '';
  }
  return options?.find((o) => o.value === String(value))?.text ?? '';
}

function readUserGroupId(raw: {
  ugid?: number;
  userGroupID?: number;
  uGID?: number;
}): number {
  const n = Number(raw.ugid ?? raw.userGroupID ?? raw.uGID ?? 0);
  return Number.isFinite(n) && n > 0 ? n : 0;
}

function pickString(row: Record<string, unknown>, camel: string, pascal: string): string {
  const raw = row[camel] ?? row[pascal];
  return typeof raw === 'string' ? raw.trim() : '';
}

function formatProjectDataCategory(category?: string | null): string {
  const map: Record<string, string> = {
    SourceData: 'Source Data',
    ExposureData: 'Exposure',
    LossData: 'Loss',
    ClaimsData: 'Claims',
    ClientDeliverable: 'Client Deliverable',
    CorrespondenceData: 'Correspondence',
    SubmissionData: 'Submission',
  };
  const key = (category ?? '').trim();
  return map[key] ?? (key || 'Source Data');
}

function sameFileName(a: string, b: string): boolean {
  return a.trim().localeCompare(b.trim(), undefined, { sensitivity: 'accent' }) === 0;
}

function todayYmdLocal(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function normalizeProjectFileRow(raw: unknown): ProjectFileRow | null {
  if (raw === null || typeof raw !== 'object') {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const id = Number(o.projectFileID ?? o.ProjectFileID);
  if (!Number.isFinite(id) || id <= 0) {
    return null;
  }
  return {
    projectFileID: id,
    originalFileName: String(o.originalFileName ?? o.OriginalFileName ?? ''),
    category: (o.category ?? o.Category ?? null) as string | null,
    versionLabel: (o.versionLabel ?? o.VersionLabel ?? null) as string | null,
    dataAsOf: (o.dataAsOf ?? o.DataAsOf ?? null) as string | null,
    isCurrent: !!(o.isCurrent ?? o.IsCurrent),
    uploadedByName: (o.uploadedByName ?? o.UploadedByName ?? null) as string | null,
    downloadUrl: (o.downloadUrl ?? o.DownloadUrl ?? null) as string | null,
    blobUrl: (o.blobUrl ?? o.BlobUrl ?? null) as string | null,
  };
}

async function uploadPdfToProjectData(
  projectId: number,
  file: File,
): Promise<ProjectFileRow[]> {
  const start = await apiPostJson<{
    sessionId: string;
    chunkSizeBytes: number;
  }>(`/api/projects/${projectId}/files/sessions`, {
    fileName: file.name,
    contentType: file.type || 'application/pdf',
    byteLength: file.size,
    category: 'CorrespondenceData',
    description: `Project report generated ${new Date().toLocaleString()}`,
    versionLabel: '1',
    dataAsOf: todayYmdLocal(),
    isCurrent: true,
  });

  const chunkSize = start.chunkSizeBytes > 0 ? start.chunkSizeBytes : 4 * 1024 * 1024;
  let offset = 0;
  let chunkIndex = 0;
  while (offset < file.size) {
    const blob = file.slice(offset, offset + chunkSize);
    const formData = new FormData();
    formData.append('chunkIndex', String(chunkIndex));
    formData.append('chunk', blob, `${file.name}.part${chunkIndex}`);
    await apiPostFormData(`/api/projects/${projectId}/files/sessions/${start.sessionId}/chunks`, formData);
    offset += chunkSize;
    chunkIndex += 1;
  }

  const rows = await apiPostJson<unknown[]>(
    `/api/projects/${projectId}/files/sessions/${start.sessionId}/complete`,
    {},
  );
  return (Array.isArray(rows) ? rows : []).map(normalizeProjectFileRow).filter((r): r is ProjectFileRow => r != null);
}

async function loadProjectReportInput(projectId: number): Promise<{
  input: ProjectReportInput;
  existingFiles: ProjectFileRow[];
}> {
  type FormModel = Record<string, unknown> & {
    clientName?: string;
    clientShortName?: string | null;
    clientBusinessUnitID?: number | '';
    clientBusinessUnitName?: string | null;
    clientBusinessClassID?: number | '';
    clientBusinessClassName?: string | null;
    clientTypeID?: number | '';
    clientTypeName?: string | null;
    clientStatusID?: number | '';
    clientStatusCode?: string | null;
    projectNumber?: string | null;
    projectName?: string;
    projectTypeID?: number;
    statusID?: number;
    isOnline?: boolean;
    renewalDate?: string | null;
    dueDate?: string | null;
    revenueAmount?: number | null;
    selectedServiceIds?: number[];
    businessUnitOptions?: unknown;
    businessClassOptions?: unknown;
    clientTypeOptions?: unknown;
    clientStatusOptions?: unknown;
    projectTypeOptions?: unknown;
    statusOptions?: unknown;
    userGroupOptions?: unknown;
  };

  const [form, filesRaw, notesRaw, calendarRaw, catalogRaw, timeSummary] = await Promise.all([
    apiGet<FormModel>(`/api/projects/form/${projectId}`),
    apiGet<unknown[]>(`/api/projects/${projectId}/files`).catch(() => []),
    apiGet<unknown[]>(`/api/projects/${projectId}/notes`).catch(() => []),
    apiGet<unknown[]>(`/api/projects/${projectId}/calendar`).catch(() => []),
    apiGet<unknown[]>('/api/projects/services-catalog').catch(() => []),
    apiGet<Record<string, unknown>>(`/api/projects/${projectId}/time/summary`).catch(() => ({})),
  ]);

  const ugid = readUserGroupId(form as { ugid?: number; userGroupID?: number; uGID?: number });
  const groupOptions = normalizeSelectOptions(form.userGroupOptions);
  const projectTypeOptions = normalizeSelectOptions(form.projectTypeOptions);
  const statusOptions = normalizeSelectOptions(form.statusOptions);
  const businessUnitOptions = normalizeSelectOptions(form.businessUnitOptions);
  const businessClassOptions = normalizeSelectOptions(form.businessClassOptions);
  const clientTypeOptions = normalizeSelectOptions(form.clientTypeOptions);
  const clientStatusOptions = normalizeSelectOptions(form.clientStatusOptions);

  let groupMembers: GroupMember[] = [];
  if (ugid > 0) {
    try {
      const members = await apiGet<GroupMember[]>(`/api/projects/user-groups/${ugid}/members`);
      groupMembers = Array.isArray(members) ? members : [];
    } catch {
      groupMembers = [];
    }
  }

  const memberById = new Map(groupMembers.map((m) => [m.userID, m]));
  const ownerName = (ownerId: number | null | undefined) => {
    if (ownerId == null || ownerId <= 0) {
      return '—';
    }
    return memberById.get(ownerId)?.userFullName?.trim() || `User ${ownerId}`;
  };

  const projectTypeOption = projectTypeOptions.find((o) => o.value === String(form.projectTypeID ?? ''));
  const projectType = projectTypeOption?.text ?? '';
  const projectTypeCode = (() => {
    const text = (projectTypeOption?.text ?? '').trim();
    if (!text) {
      return '';
    }
    const sep = text.indexOf(' - ');
    return (sep > 0 ? text.slice(0, sep) : text).trim();
  })();

  const selectedServiceIds = Array.isArray(form.selectedServiceIds) ? form.selectedServiceIds : [];
  const catalog = (Array.isArray(catalogRaw) ? catalogRaw : []).map((row) => {
    const o = row as Record<string, unknown>;
    return {
      serviceID: Number(o.serviceID ?? o.ServiceID ?? 0),
      serviceName: String(o.serviceName ?? o.ServiceName ?? ''),
      categoryName: (o.categoryName ?? o.CategoryName ?? null) as string | null,
      serviceCategoryDescription: (o.serviceCategoryDescription ??
        o.ServiceCategoryDescription ??
        null) as string | null,
    };
  });

  const existingFiles = (Array.isArray(filesRaw) ? filesRaw : [])
    .map(normalizeProjectFileRow)
    .filter((r): r is ProjectFileRow => r != null);

  const byAnalystRaw = (timeSummary.byAnalyst ?? timeSummary.ByAnalyst ?? []) as unknown[];
  const byTaskRaw = (timeSummary.byTask ?? timeSummary.ByTask ?? []) as unknown[];
  const timeByAnalyst = byAnalystRaw.map((r) => {
    const o = r as Record<string, unknown>;
    return {
      label: String(o.label ?? o.Label ?? ''),
      detail: (o.detail ?? o.Detail ?? null) as string | null,
      hours: Number(o.hours ?? o.Hours ?? 0),
    };
  });
  const timeByTask = byTaskRaw.map((r) => {
    const o = r as Record<string, unknown>;
    return {
      label: String(o.label ?? o.Label ?? ''),
      detail: (o.detail ?? o.Detail ?? null) as string | null,
      hours: Number(o.hours ?? o.Hours ?? 0),
    };
  });
  const totalHours = Number(
    timeSummary.totalHours ?? timeSummary.TotalHours ?? timeByAnalyst.reduce((s, r) => s + r.hours, 0),
  );

  const notes = (Array.isArray(notesRaw) ? notesRaw : []).map((n) => {
    const o = n as Record<string, unknown>;
    return {
      noteType: String(o.noteType ?? o.NoteType ?? 'General'),
      noteDate: (o.noteDate ?? o.NoteDate ?? null) as string | null,
      userFullName: (o.userFullName ?? o.UserFullName ?? null) as string | null,
      isPinned: !!(o.isPinned ?? o.IsPinned),
      noteText: String(o.noteText ?? o.NoteText ?? ''),
    };
  });

  const calendar = (Array.isArray(calendarRaw) ? calendarRaw : []).map((c) => {
    const o = c as Record<string, unknown>;
    return {
      itemType: String(o.itemType ?? o.ItemType ?? 'Task'),
      title: String(o.title ?? o.Title ?? '').trim() || '—',
      owner: ownerName(Number(o.ownerUserID ?? o.OwnerUserID ?? 0)),
      receivedDate:
        formatCalendarDateDisplay(toInputDateValue(o.receivedDate ?? o.ReceivedDate)) ||
        String(o.receivedDate ?? o.ReceivedDate ?? '—'),
      completedDate:
        formatCalendarDateDisplay(toInputDateValue(o.completedDate ?? o.CompletedDate)) ||
        String(o.completedDate ?? o.CompletedDate ?? '—'),
      notes: String(o.notes ?? o.Notes ?? '').trim() || '—',
    };
  });

  const input: ProjectReportInput = {
    client: {
      clientName: String(form.clientName ?? ''),
      clientShortName: form.clientShortName,
      businessUnit:
        optionTextForValue(businessUnitOptions, form.clientBusinessUnitID as number | '') ||
        String(form.clientBusinessUnitName ?? ''),
      businessClass:
        formatBusinessClassDisplay(
          optionTextForValue(businessClassOptions, form.clientBusinessClassID as number | '') ||
            String(form.clientBusinessClassName ?? ''),
        ) || String(form.clientBusinessClassName ?? ''),
      clientType:
        optionTextForValue(clientTypeOptions, form.clientTypeID as number | '') ||
        String(form.clientTypeName ?? ''),
      clientStatus:
        optionTextForValue(clientStatusOptions, form.clientStatusID as number | '') ||
        String(form.clientStatusCode ?? ''),
    },
    project: {
      projectId,
      projectNumber: form.projectNumber,
      projectName: String(form.projectName ?? ''),
      projectType,
      projectTypeCode,
      userGroup: groupOptions.find((o) => o.value === String(ugid))?.text ?? (ugid > 0 ? `User Group ${ugid}` : ''),
      status: statusOptions.find((o) => o.value === String(form.statusID ?? ''))?.text ?? '',
      isOnline: !!form.isOnline,
      renewalDate: form.renewalDate,
      dueDate: form.dueDate,
      latestRevenue: form.revenueAmount == null ? null : Number(form.revenueAmount),
    },
    userGroupMembers: groupMembers.map((m) => {
      const row = m as GroupMember & Record<string, unknown>;
      return {
        name: pickString(row, 'userFullName', 'UserFullName') || `User ${m.userID}`,
        title: pickString(row, 'title', 'Title') || '—',
        role: pickString(row, 'userRoleName', 'UserRoleName') || '—',
        phone: pickString(row, 'phone', 'Phone') || '—',
        email: pickString(row, 'email', 'Email') || '—',
      };
    }),
    dataSources: existingFiles.map((f) => ({
      fileName: f.originalFileName,
      category: formatProjectDataCategory(f.category),
      version: (f.versionLabel ?? '').trim() || '—',
      dataAsOf: formatCalendarDateDisplay(f.dataAsOf ?? undefined) || (f.dataAsOf ?? '—'),
      active: !!f.isCurrent,
      uploadedBy: (f.uploadedByName ?? '').trim() || '—',
    })),
    services: catalog
      .filter((s) => selectedServiceIds.includes(s.serviceID))
      .map((s) => ({
        serviceName: s.serviceName,
        category: (s.serviceCategoryDescription ?? '').trim() || (s.categoryName ?? '').trim() || '—',
      })),
    calendar,
    timeByAnalyst,
    timeByTask,
    totalHours,
    notes: notes
      .slice()
      .sort((a, b) => Number(b.isPinned) - Number(a.isPinned))
      .map((n) => ({
        noteType: (n.noteType ?? '').trim() || 'General',
        noteDate: formatCalendarDateDisplay(n.noteDate ?? undefined) || (n.noteDate ?? '—'),
        author: (n.userFullName ?? '').trim() || '—',
        pinned: !!n.isPinned,
        text: (n.noteText ?? '').trim(),
      })),
  };

  return { input, existingFiles };
}

async function promptAndSaveReportPdf(options: {
  projectId: number;
  defaultFileName: string;
  pdfBlob: Blob;
  existingFiles: ProjectFileRow[];
  requireSave: boolean;
}): Promise<{ saved: boolean; fileName: string; files: ProjectFileRow[]; pdfFile: File | null }> {
  const { projectId, defaultFileName, pdfBlob, existingFiles, requireSave } = options;

  const saveToStorage = requireSave
    ? true
    : window.confirm(
        'Save this project report to project data storage (Azure Blob)?\n\nClick Cancel to skip storage and only open the print dialog.',
      );

  if (!saveToStorage) {
    return { saved: false, fileName: defaultFileName, files: existingFiles, pdfFile: null };
  }

  if (requireSave) {
    // Still confirm so the user knows a blob save is about to happen.
    const proceed = window.confirm(
      'Save this project report to project data storage (Azure Blob), then open Outlook with the PDF attached?',
    );
    if (!proceed) {
      return { saved: false, fileName: defaultFileName, files: existingFiles, pdfFile: null };
    }
  }

  const enteredName = window.prompt('Enter a file name for the report:', defaultFileName);
  if (enteredName == null) {
    return { saved: false, fileName: defaultFileName, files: existingFiles, pdfFile: null };
  }

  const fileName = sanitizeProjectReportFileName(enteredName, defaultFileName);
  const pdfFile = new File([pdfBlob], fileName, { type: 'application/pdf' });
  const duplicates = existingFiles.filter((f) => sameFileName(f.originalFileName || '', fileName));

  if (duplicates.length > 0) {
    const overwrite = window.confirm(
      `A file named "${fileName}" already exists in project data (${duplicates.length} matching file${duplicates.length === 1 ? '' : 's'}).\n\nOverwrite the existing file${duplicates.length === 1 ? '' : 's'}?`,
    );
    if (!overwrite) {
      return { saved: false, fileName, files: existingFiles, pdfFile };
    }
    for (const dup of duplicates) {
      await apiDelete(`/api/projects/${projectId}/files/${dup.projectFileID}`);
    }
  }

  const files = await uploadPdfToProjectData(projectId, pdfFile);
  return { saved: true, fileName, files, pdfFile };
}

/**
 * Generate a project PDF report (optionally save to Azure and/or open Outlook).
 * Used by Project page Generate Report and Home list context menu.
 */
export async function runProjectReportWorkflow(options: {
  projectId: number;
  mode: ProjectReportRunMode;
  flash: FlashFn;
  appPublicBaseUrl?: string;
  onProjectFilesUpdated?: (files: ProjectFileRow[]) => void;
}): Promise<void> {
  const { projectId, mode, flash, appPublicBaseUrl, onProjectFilesUpdated } = options;
  if (projectId <= 0) {
    flash('error', 'Select a saved project before generating a report.');
    return;
  }

  const { input, existingFiles } = await loadProjectReportInput(projectId);
  const defaultFileName = suggestedProjectReportFileName(input);
  const pdfBlob = await createProjectReportPdfBlob(input);

  const saveResult = await promptAndSaveReportPdf({
    projectId,
    defaultFileName,
    pdfBlob,
    existingFiles,
    requireSave: mode === 'saveAndSendAttachment',
  });

  if (saveResult.saved) {
    onProjectFilesUpdated?.(saveResult.files);
  }

  if (mode === 'saveAndSendAttachment') {
    if (!saveResult.saved || !saveResult.pdfFile) {
      flash('success', 'Report was not saved, so no email draft was created.');
      return;
    }

    const ccEmails = await loadProjectCatCcEmails(projectId);
    const savedFile =
      saveResult.files.find((f) => sameFileName(f.originalFileName || '', saveResult.fileName)) ??
      saveResult.files[0];
    const downloadUrl = (savedFile?.downloadUrl || savedFile?.blobUrl || '').trim();
    const base = (appPublicBaseUrl || window.location.origin).replace(/\/$/, '');
    const projectPageUrl = `${base}/projects/${projectId}?tab=projectData`;
    const bodyLines = [
      'Please find the project report attached.',
      '',
      downloadUrl ? `Download: [${saveResult.fileName}](${downloadUrl})` : `File: ${saveResult.fileName}`,
      `Project data: [Open project data](${projectPageUrl})`,
    ];
    const bodyText = bodyLines
      .map((line) => {
        const m = line.match(/^(.+?):\s*\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)$/);
        return m ? `${m[1]}: ${m[2]}` : line;
      })
      .join('\r\n');

    const emlBase = saveResult.fileName.toLowerCase().endsWith('.pdf')
      ? saveResult.fileName.slice(0, -4)
      : saveResult.fileName;

    await openOutlookDraftWithAttachment({
      subject: saveResult.fileName,
      bodyText,
      bodyHtml: buildOutlookHtmlFromLines(bodyLines),
      ccEmails,
      fileName: saveResult.fileName,
      attachmentBlob: pdfBlob,
      attachmentFileName: saveResult.fileName,
      attachmentContentType: 'application/pdf',
    });

    window.alert(
      `The Outlook email draft (${emlBase}.eml) has been created.\n\nOpen it from your Downloads folder to review and send.`,
    );
    flash(
      'success',
      `Report saved as "${saveResult.fileName}". Email draft (${emlBase}.eml) downloaded.`,
    );
    return;
  }

  // interactive (same as Project page Generate Report button)
  if (saveResult.saved) {
    const sendEmail = window.confirm(
      "Open Outlook to email this project report?\n\nCAT department members of this project's user group will be added on CC. The message will include a download link to the file in project data.",
    );
    if (sendEmail) {
      try {
        const ccEmails = await loadProjectCatCcEmails(projectId);
        const savedFile =
          saveResult.files.find((f) => sameFileName(f.originalFileName || '', saveResult.fileName)) ??
          saveResult.files[0];
        const downloadUrl = (savedFile?.downloadUrl || savedFile?.blobUrl || '').trim();
        const base = (appPublicBaseUrl || window.location.origin).replace(/\/$/, '');
        const projectPageUrl = `${base}/projects/${projectId}?tab=projectData`;
        const clientName = (input.client.clientName ?? '').trim();
        const projectName = (input.project.projectName ?? '').trim();
        const subjectBase =
          [clientName, projectName].filter(Boolean).join(' — ').trim() || 'Project report';
        const subject = `${subjectBase}: Project Report`;
        const bodyLines = [
          'A project report has been saved to project data storage.',
          '',
          downloadUrl ? `Download: [${saveResult.fileName}](${downloadUrl})` : `File: ${saveResult.fileName}`,
          `Project data: [Open project data](${projectPageUrl})`,
          '',
          'Please review and follow up as needed.',
        ];
        const bodyText = bodyLines
          .map((line) => {
            const m = line.match(/^(.+?):\s*\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)$/);
            return m ? `${m[1]}: ${m[2]}` : line;
          })
          .join('\r\n');

        await openOutlookDraftWithAttachment({
          subject,
          bodyText,
          bodyHtml: buildOutlookHtmlFromLines(bodyLines),
          ccEmails,
          fileName: saveResult.fileName,
        });
      } catch (emailErr) {
        flash(
          'error',
          emailErr instanceof ApiError
            ? emailErr.message
            : 'Report was saved, but Outlook could not be opened with CAT CC recipients.',
        );
      }
    }
  }

  exportProjectReportPdf(input);
  flash(
    'success',
    saveResult.saved
      ? `Report saved to project data as "${saveResult.fileName}". Print dialog opened — use Save as PDF for a local copy if needed.`
      : 'Project report opened — use Save as PDF in the print dialog.',
  );
}
