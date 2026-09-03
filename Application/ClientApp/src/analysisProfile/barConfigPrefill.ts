import type { BarConfigDocument } from './barConfigTypes';

export interface AnalysisProfilePrefill {
  projectName: string;
  clientName: string;
  clientId: number;
  datasetID: string;
  clientDateAsOf: string;
  activeFileName: string;
  email?: string;
}

function setQuestionValue(doc: BarConfigDocument, name: string, value: string) {
  const row = doc.questions.find((q) => q.name === name);
  if (row) {
    row.value = value;
  } else {
    doc.questions.push({ name, value });
  }
}

/** Apply project + active project-data file values into a BAR config document. */
export function applyBarConfigPrefill(doc: BarConfigDocument, prefill: AnalysisProfilePrefill): BarConfigDocument {
  const next = structuredClone(doc);
  const clientName = prefill.clientName.trim();
  const projectName = prefill.projectName.trim();
  const datasetId = prefill.datasetID.trim();
  const dateAsOf = prefill.clientDateAsOf.trim();

  next.ClientName = clientName;
  next.datasetID = datasetId;
  next.clientDateAsOf = dateAsOf;
  if (prefill.email?.trim()) {
    next.email = prefill.email.trim();
  }

  setQuestionValue(next, 'datasetID', datasetId);
  setQuestionValue(next, 'ClientName', clientName);
  setQuestionValue(next, 'clientDateAsOf', dateAsOf);
  if (prefill.email?.trim()) {
    setQuestionValue(next, 'Email', prefill.email.trim());
  }
  if (prefill.activeFileName.trim()) {
    setQuestionValue(next, 'uploadedFiles', prefill.activeFileName.trim());
    if (next.uploadedFiles.length === 0) {
      next.uploadedFiles = [prefill.activeFileName.trim()];
    } else {
      next.uploadedFiles[0] = prefill.activeFileName.trim();
    }
  }

  for (const entry of next.jsonConfig?.AnalysisConfigs ?? []) {
    entry.Project.ProjectName = projectName;
    entry.Project.ClientName = clientName;
    entry.Project.ClientHeaderName = clientName;
    if (prefill.clientId > 0) {
      entry.Project.ClientId = prefill.clientId;
    }
    entry.Project.DateAsOf = dateAsOf;
  }

  return next;
}

export function isRmsAnalysisEntry(entry: BarConfigDocument['jsonConfig']['AnalysisConfigs'][number]): boolean {
  return entry.Project.RMS || entry.Import.some((row) => row.ModelName === 'RMS' || row.Model === 2);
}

export function isAirAnalysisEntry(entry: BarConfigDocument['jsonConfig']['AnalysisConfigs'][number]): boolean {
  return entry.Project.AIR || entry.Import.some((row) => row.ModelName === 'AIR' || row.Model === 1);
}
