import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ApiError, apiGet, apiPostJson } from '../api';
import type { FlashFn } from '../App';
import { toApiDateValue, toInputDateValue } from '../utils/dateInput';
import type {
  BarAirAnalysisConfig,
  BarAnalysisConfigEntry,
  BarConfigDocument,
  BarImportRow,
  BarRmsAnalysisConfig,
} from '../analysisProfile/barConfigTypes';
import { applyBarConfigPrefill, isAirAnalysisEntry, isRmsAnalysisEntry } from '../analysisProfile/barConfigPrefill';

type VendorTab = 'moodys' | 'verisk';

export interface ActiveProjectFilePrefill {
  versionLabel: string;
  dataAsOf: string;
  originalFileName: string;
  projectFileID: number;
}

interface ProjectAnalysisProfileTabProps {
  projectId: number;
  projectName: string;
  clientName: string;
  clientId: number;
  activeProjectFile: ActiveProjectFilePrefill | null;
  isActive: boolean;
  flash: FlashFn;
}

function linesFromArray(values: string[]): string {
  return values.join('\n');
}

function arrayFromLines(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function FieldRow({
  label,
  children,
  hint,
}: {
  label: string;
  children: ReactNode;
  hint?: string;
}) {
  return (
    <div className="analysis-profile-field">
      <label>{label}</label>
      {children}
      {hint ? <span className="field-help">{hint}</span> : null}
    </div>
  );
}

function BoolSelect({
  value,
  onChange,
  id,
}: {
  value: string;
  onChange: (v: string) => void;
  id?: string;
}) {
  return (
    <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="True">True</option>
      <option value="False">False</option>
    </select>
  );
}

/** Keep root + questions + Project entries aligned for both Moodys and Verisk JSON. */
function applySharedClientFields(
  config: BarConfigDocument,
  patch: { ClientName?: string; datasetID?: string; clientDateAsOf?: string },
): BarConfigDocument {
  const next: BarConfigDocument = {
    ...config,
    questions: config.questions.map((q) => ({ ...q })),
    jsonConfig: {
      ...config.jsonConfig,
      AnalysisConfigs: config.jsonConfig.AnalysisConfigs.map((entry) => ({
        ...entry,
        Project: { ...entry.Project },
      })),
    },
  };

  if (patch.ClientName !== undefined) {
    next.ClientName = patch.ClientName;
    const q = next.questions.find((row) => row.name === 'ClientName');
    if (q) {
      q.value = patch.ClientName;
    } else {
      next.questions.push({ name: 'ClientName', value: patch.ClientName });
    }
    for (const entry of next.jsonConfig.AnalysisConfigs) {
      entry.Project.ClientName = patch.ClientName;
      entry.Project.ClientHeaderName = patch.ClientName;
    }
  }

  if (patch.datasetID !== undefined) {
    next.datasetID = patch.datasetID;
    const q = next.questions.find((row) => row.name === 'datasetID');
    if (q) {
      q.value = patch.datasetID;
    } else {
      next.questions.push({ name: 'datasetID', value: patch.datasetID });
    }
  }

  if (patch.clientDateAsOf !== undefined) {
    next.clientDateAsOf = patch.clientDateAsOf;
    const q = next.questions.find((row) => row.name === 'clientDateAsOf');
    if (q) {
      q.value = patch.clientDateAsOf;
    } else {
      next.questions.push({ name: 'clientDateAsOf', value: patch.clientDateAsOf });
    }
    for (const entry of next.jsonConfig.AnalysisConfigs) {
      entry.Project.DateAsOf = patch.clientDateAsOf;
    }
  }

  return next;
}

const AIR_PERIL_FIELDS: Array<{ key: keyof BarAirAnalysisConfig; label: string }> = [
  { key: 'Wind', label: 'Wind' },
  { key: 'TropicalCyclone', label: 'Tropical Cyclone' },
  { key: 'StormSurge', label: 'Storm Surge' },
  { key: 'Earthquake', label: 'Earthquake' },
  { key: 'EarthquakeShake', label: 'Earthquake Shake' },
  { key: 'FireFollowing', label: 'Fire Following' },
  { key: 'SprinklerLeakage', label: 'Sprinkler Leakage' },
  { key: 'Landslide', label: 'Landslide' },
  { key: 'Tsunami', label: 'Tsunami' },
  { key: 'Liquefaction', label: 'Liquefaction' },
  { key: 'PrecipitationFlood', label: 'Precipitation Flood' },
  { key: 'SevereStorm', label: 'Severe Storm' },
  { key: 'SevereThunderStorm', label: 'Severe Thunderstorm' },
  { key: 'Hail', label: 'Hail' },
  { key: 'StraightLineWinds', label: 'Straight Line Winds' },
  { key: 'Tornado', label: 'Tornado' },
  { key: 'WinterStorm', label: 'Winter Storm' },
  { key: 'OtherPerils', label: 'Other Perils' },
  { key: 'InlandFlood', label: 'Inland Flood' },
  { key: 'WildFire', label: 'Wild Fire' },
  { key: 'Terrorism', label: 'Terrorism' },
  { key: 'CBRNDomestic', label: 'CBRN Domestic' },
  { key: 'ConventionalDomestic', label: 'Conventional Domestic' },
  { key: 'CoastalFlood', label: 'Coastal Flood' },
  { key: 'MoveMarineGC', label: 'Move Marine GC' },
  { key: 'DemandSurge', label: 'Demand Surge' },
  { key: 'DemandSurgePct', label: 'Demand Surge %' },
  { key: 'Disaggregation', label: 'Disaggregation' },
  { key: 'IncludexDS', label: 'Include xDS' },
  { key: 'OnFloodPlains', label: 'On Flood Plains' },
  { key: 'OffFloodPlains', label: 'Off Flood Plains' },
  { key: 'FZDefault', label: 'FZ Default' },
  { key: 'FZZone1', label: 'FZ Zone 1' },
  { key: 'FZZone2', label: 'FZ Zone 2' },
  { key: 'FZZone3', label: 'FZ Zone 3' },
  { key: 'FZZone4', label: 'FZ Zone 4' },
  { key: 'FZOnFP', label: 'FZ On FP' },
  { key: 'FZOffFP', label: 'FZ Off FP' },
  { key: 'AirExportELT', label: 'AIR Export ELT' },
];

function MoodysForm({
  config,
  onChange,
  rmsEntries,
}: {
  config: BarConfigDocument;
  onChange: (next: BarConfigDocument) => void;
  rmsEntries: BarAnalysisConfigEntry[];
}) {
  const updateRoot = (patch: Partial<BarConfigDocument>) => onChange({ ...config, ...patch });

  const updateRmsEntry = (index: number, patch: Partial<BarAnalysisConfigEntry>) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(rmsEntries[index]);
    if (globalIndex < 0) {
      return;
    }
    entries[globalIndex] = { ...entries[globalIndex], ...patch };
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  const updateRmsImport = (entryIndex: number, importIndex: number, patch: Partial<BarImportRow>) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(rmsEntries[entryIndex]);
    if (globalIndex < 0) {
      return;
    }
    const entry = { ...entries[globalIndex] };
    const imports = [...entry.Import];
    imports[importIndex] = { ...imports[importIndex], ...patch };
    entry.Import = imports;
    entries[globalIndex] = entry;
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  const updateRmsAnalysis = (
    entryIndex: number,
    importIndex: number,
    patch: Partial<BarRmsAnalysisConfig>,
  ) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(rmsEntries[entryIndex]);
    if (globalIndex < 0) {
      return;
    }
    const entry = { ...entries[globalIndex] };
    const imports = [...entry.Import];
    const row = imports[importIndex];
    if (!row.RmsAnalysisConfig) {
      return;
    }
    imports[importIndex] = {
      ...row,
      RmsAnalysisConfig: { ...row.RmsAnalysisConfig, ...patch },
    };
    entry.Import = imports;
    entries[globalIndex] = entry;
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  return (
    <div className="analysis-profile-form">
      <h3>Moodys (RMS) configuration</h3>
      <p className="field-help">
        Keys for RMS modeling — worker credentials, file paths, and per-import RMS analysis settings.
      </p>

      <fieldset className="analysis-profile-section">
        <legend>Worker &amp; submission</legend>
        <div className="analysis-profile-grid">
          <FieldRow label="workerTag">
            <input value={config.workerTag} onChange={(e) => updateRoot({ workerTag: e.target.value })} />
          </FieldRow>
          <FieldRow label="credentialId">
            <input value={config.credentialId} onChange={(e) => updateRoot({ credentialId: e.target.value })} />
          </FieldRow>
          <FieldRow label="Priority">
            <input value={config.Priority} onChange={(e) => updateRoot({ Priority: e.target.value })} />
          </FieldRow>
          <FieldRow label="email">
            <input value={config.email} onChange={(e) => updateRoot({ email: e.target.value })} />
          </FieldRow>
          <FieldRow label="metadata">
            <BoolSelect value={config.metadata} onChange={(v) => updateRoot({ metadata: v })} />
          </FieldRow>
          <FieldRow label="fieldVerification">
            <BoolSelect value={config.fieldVerification} onChange={(v) => updateRoot({ fieldVerification: v })} />
          </FieldRow>
          <FieldRow label="dataTransformation">
            <BoolSelect value={config.dataTransformation} onChange={(v) => updateRoot({ dataTransformation: v })} />
          </FieldRow>
          <FieldRow label="DatabaseSuffix">
            <input
              value={config.DatabaseSuffix ?? ''}
              onChange={(e) => updateRoot({ DatabaseSuffix: e.target.value || null })}
            />
          </FieldRow>
        </div>
      </fieldset>

      <fieldset className="analysis-profile-section">
        <legend>RMS file paths</legend>
        <FieldRow label="uploadedFiles (one path per line)">
          <textarea
            rows={3}
            value={linesFromArray(config.uploadedFiles)}
            onChange={(e) => updateRoot({ uploadedFiles: arrayFromLines(e.target.value) })}
          />
        </FieldRow>
        <FieldRow label="rmsAccountFilename">
          <textarea
            rows={3}
            value={linesFromArray(config.rmsAccountFilename)}
            onChange={(e) => updateRoot({ rmsAccountFilename: arrayFromLines(e.target.value) })}
          />
        </FieldRow>
        <FieldRow label="rmsLocationFilename">
          <textarea
            rows={3}
            value={linesFromArray(config.rmsLocationFilename)}
            onChange={(e) => updateRoot({ rmsLocationFilename: arrayFromLines(e.target.value) })}
          />
        </FieldRow>
        <FieldRow label="rmsReinsuranceFilename">
          <input
            value={config.rmsReinsuranceFilename}
            onChange={(e) => updateRoot({ rmsReinsuranceFilename: e.target.value })}
          />
        </FieldRow>
      </fieldset>

      {rmsEntries.map((entry, entryIndex) => (
        <fieldset key={`rms-${entryIndex}`} className="analysis-profile-section">
          <legend>
            RMS analysis config #{entryIndex + 1} — {entry.Project.Type || entry.Import[0]?.Portfolio || 'RMS'}
          </legend>
          <div className="analysis-profile-grid">
            <FieldRow label="ProjectName">
              <input
                value={entry.Project.ProjectName}
                onChange={(e) =>
                  updateRmsEntry(entryIndex, {
                    Project: { ...entry.Project, ProjectName: e.target.value },
                  })
                }
              />
            </FieldRow>
            <FieldRow label="Project Type">
              <input
                value={entry.Project.Type}
                onChange={(e) =>
                  updateRmsEntry(entryIndex, {
                    Project: { ...entry.Project, Type: e.target.value },
                  })
                }
              />
            </FieldRow>
            <FieldRow label="DateAsOf">
              <input
                type="date"
                value={toInputDateValue(entry.Project.DateAsOf)}
                onChange={(e) =>
                  updateRmsEntry(entryIndex, {
                    Project: { ...entry.Project, DateAsOf: e.target.value || '' },
                  })
                }
              />
            </FieldRow>
            <FieldRow label="SubmittedUser">
              <input
                value={entry.SubmittedUser}
                onChange={(e) => updateRmsEntry(entryIndex, { SubmittedUser: e.target.value })}
              />
            </FieldRow>
            <FieldRow label="UserShortName">
              <input
                value={entry.UserShortName}
                onChange={(e) => updateRmsEntry(entryIndex, { UserShortName: e.target.value })}
              />
            </FieldRow>
          </div>

          {entry.Import.map((imp, importIndex) => (
            <div key={`rms-import-${importIndex}`} className="analysis-profile-import-block">
              <h4>Import row {importIndex + 1}</h4>
              <div className="analysis-profile-grid">
                <FieldRow label="Portfolio">
                  <input
                    value={imp.Portfolio}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { Portfolio: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="DisplayName">
                  <input
                    value={imp.DisplayName}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { DisplayName: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="ModelVersion">
                  <input
                    value={imp.ModelVersion}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { ModelVersion: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="ContractFile">
                  <input
                    value={imp.ContractFile}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { ContractFile: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="LocationFile">
                  <input
                    value={imp.LocationFile}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { LocationFile: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="ReInsuranceFile">
                  <input
                    value={imp.ReInsuranceFile}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { ReInsuranceFile: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="TargetExposureDB">
                  <input
                    value={imp.TargetExposureDB}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { TargetExposureDB: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="TargetResultDB">
                  <input
                    value={imp.TargetResultDB}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { TargetResultDB: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="UserPortName">
                  <input
                    value={imp.UserPortName}
                    onChange={(e) => updateRmsImport(entryIndex, importIndex, { UserPortName: e.target.value })}
                  />
                </FieldRow>
                {imp.RmsAnalysisConfig ? (
                  <>
                    <FieldRow label="rmsAnalysisProfile">
                      <input
                        value={imp.RmsAnalysisConfig.rmsAnalysisProfile}
                        onChange={(e) =>
                          updateRmsAnalysis(entryIndex, importIndex, { rmsAnalysisProfile: e.target.value })
                        }
                      />
                    </FieldRow>
                    <FieldRow label="rmsOutputProfile">
                      <input
                        value={imp.RmsAnalysisConfig.rmsOutputProfile}
                        onChange={(e) =>
                          updateRmsAnalysis(entryIndex, importIndex, { rmsOutputProfile: e.target.value })
                        }
                      />
                    </FieldRow>
                    <FieldRow label="rmsEventRate">
                      <input
                        value={imp.RmsAnalysisConfig.rmsEventRate}
                        onChange={(e) =>
                          updateRmsAnalysis(entryIndex, importIndex, { rmsEventRate: e.target.value })
                        }
                      />
                    </FieldRow>
                  </>
                ) : null}
              </div>
            </div>
          ))}
        </fieldset>
      ))}
    </div>
  );
}

function VeriskForm({
  config,
  onChange,
  airEntries,
}: {
  config: BarConfigDocument;
  onChange: (next: BarConfigDocument) => void;
  airEntries: BarAnalysisConfigEntry[];
}) {
  const updateRoot = (patch: Partial<BarConfigDocument>) => onChange({ ...config, ...patch });

  const updateAirEntry = (index: number, patch: Partial<BarAnalysisConfigEntry>) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(airEntries[index]);
    if (globalIndex < 0) {
      return;
    }
    entries[globalIndex] = { ...entries[globalIndex], ...patch };
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  const updateAirImport = (entryIndex: number, importIndex: number, patch: Partial<BarImportRow>) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(airEntries[entryIndex]);
    if (globalIndex < 0) {
      return;
    }
    const entry = { ...entries[globalIndex] };
    const imports = [...entry.Import];
    imports[importIndex] = { ...imports[importIndex], ...patch };
    entry.Import = imports;
    entries[globalIndex] = entry;
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  const updateAirAnalysis = (
    entryIndex: number,
    importIndex: number,
    patch: Partial<BarAirAnalysisConfig>,
  ) => {
    const entries = [...config.jsonConfig.AnalysisConfigs];
    const globalIndex = config.jsonConfig.AnalysisConfigs.indexOf(airEntries[entryIndex]);
    if (globalIndex < 0) {
      return;
    }
    const entry = { ...entries[globalIndex] };
    const imports = [...entry.Import];
    const row = imports[importIndex];
    if (!row.AirAnalysisConfig) {
      return;
    }
    imports[importIndex] = {
      ...row,
      AirAnalysisConfig: { ...row.AirAnalysisConfig, ...patch },
    };
    entry.Import = imports;
    entries[globalIndex] = entry;
    onChange({
      ...config,
      jsonConfig: { ...config.jsonConfig, AnalysisConfigs: entries },
    });
  };

  return (
    <div className="analysis-profile-form">
      <h3>Verisk (AIR) configuration</h3>
      <p className="field-help">
        Keys for AIR modeling — file paths, import settings, and peril / analysis options.
      </p>

      <fieldset className="analysis-profile-section">
        <legend>Contact</legend>
        <div className="analysis-profile-grid">
          <FieldRow label="email">
            <input value={config.email} onChange={(e) => updateRoot({ email: e.target.value })} />
          </FieldRow>
        </div>
      </fieldset>

      <fieldset className="analysis-profile-section">
        <legend>AIR file paths</legend>
        <FieldRow label="airAccountFilename">
          <textarea
            rows={3}
            value={linesFromArray(config.airAccountFilename)}
            onChange={(e) => updateRoot({ airAccountFilename: arrayFromLines(e.target.value) })}
          />
        </FieldRow>
        <FieldRow label="airLocationFilename">
          <textarea
            rows={3}
            value={linesFromArray(config.airLocationFilename)}
            onChange={(e) => updateRoot({ airLocationFilename: arrayFromLines(e.target.value) })}
          />
        </FieldRow>
        <FieldRow label="airReinsuranceFilename">
          <input
            value={config.airReinsuranceFilename}
            onChange={(e) => updateRoot({ airReinsuranceFilename: e.target.value })}
          />
        </FieldRow>
      </fieldset>

      {airEntries.map((entry, entryIndex) => (
        <fieldset key={`air-${entryIndex}`} className="analysis-profile-section">
          <legend>
            AIR analysis config #{entryIndex + 1} — {entry.Project.Type || entry.Import[0]?.Portfolio || 'AIR'}
          </legend>
          <div className="analysis-profile-grid">
            <FieldRow label="ProjectName">
              <input
                value={entry.Project.ProjectName}
                onChange={(e) =>
                  updateAirEntry(entryIndex, {
                    Project: { ...entry.Project, ProjectName: e.target.value },
                  })
                }
              />
            </FieldRow>
            <FieldRow label="DateAsOf">
              <input
                type="date"
                value={toInputDateValue(entry.Project.DateAsOf)}
                onChange={(e) =>
                  updateAirEntry(entryIndex, {
                    Project: { ...entry.Project, DateAsOf: e.target.value || '' },
                  })
                }
              />
            </FieldRow>
            <FieldRow label="SubmittedUser">
              <input
                value={entry.SubmittedUser}
                onChange={(e) => updateAirEntry(entryIndex, { SubmittedUser: e.target.value })}
              />
            </FieldRow>
            <FieldRow label="UserShortName">
              <input
                value={entry.UserShortName}
                onChange={(e) => updateAirEntry(entryIndex, { UserShortName: e.target.value })}
              />
            </FieldRow>
          </div>

          {entry.Import.map((imp, importIndex) => (
            <div key={`air-import-${importIndex}`} className="analysis-profile-import-block">
              <h4>Import row {importIndex + 1}</h4>
              <div className="analysis-profile-grid">
                <FieldRow label="Portfolio">
                  <input
                    value={imp.Portfolio}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { Portfolio: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="ModelVersion">
                  <input
                    value={imp.ModelVersion}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { ModelVersion: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="ContractFile">
                  <input
                    value={imp.ContractFile}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { ContractFile: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="LocationFile">
                  <input
                    value={imp.LocationFile}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { LocationFile: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="TargetExposureDB">
                  <input
                    value={imp.TargetExposureDB}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { TargetExposureDB: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="TargetResultDB">
                  <input
                    value={imp.TargetResultDB}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { TargetResultDB: e.target.value })}
                  />
                </FieldRow>
                <FieldRow label="UserPortName">
                  <input
                    value={imp.UserPortName}
                    onChange={(e) => updateAirImport(entryIndex, importIndex, { UserPortName: e.target.value })}
                  />
                </FieldRow>
              </div>

              {imp.AirAnalysisConfig ? (
                <fieldset className="analysis-profile-section analysis-profile-perils">
                  <legend>AIR peril &amp; analysis options</legend>
                  <div className="analysis-profile-peril-grid">
                    {AIR_PERIL_FIELDS.map(({ key, label }) => (
                      <label key={key} className="analysis-profile-check">
                        <input
                          type="checkbox"
                          checked={imp.AirAnalysisConfig![key] as boolean}
                          onChange={(e) =>
                            updateAirAnalysis(entryIndex, importIndex, {
                              [key]: e.target.checked,
                            } as Partial<BarAirAnalysisConfig>)
                          }
                        />
                        {label}
                      </label>
                    ))}
                  </div>
                  <div className="analysis-profile-grid">
                    <FieldRow label="StormSurgePercent">
                      <input
                        value={imp.AirAnalysisConfig.StormSurgePercent}
                        onChange={(e) =>
                          updateAirAnalysis(entryIndex, importIndex, { StormSurgePercent: e.target.value })
                        }
                      />
                    </FieldRow>
                    <FieldRow label="DemandSurgePercent">
                      <input
                        value={imp.AirAnalysisConfig.DemandSurgePercent}
                        onChange={(e) =>
                          updateAirAnalysis(entryIndex, importIndex, { DemandSurgePercent: e.target.value })
                        }
                      />
                    </FieldRow>
                    <FieldRow label="PrecipitationFloodPercentage">
                      <input
                        value={imp.AirAnalysisConfig.PrecipitationFloodPercentage}
                        onChange={(e) =>
                          updateAirAnalysis(entryIndex, importIndex, {
                            PrecipitationFloodPercentage: e.target.value,
                          })
                        }
                      />
                    </FieldRow>
                    <FieldRow label="ModelVersion">
                      <input
                        value={imp.AirAnalysisConfig.ModelVersion}
                        onChange={(e) =>
                          updateAirAnalysis(entryIndex, importIndex, { ModelVersion: e.target.value })
                        }
                      />
                    </FieldRow>
                  </div>
                </fieldset>
              ) : null}
            </div>
          ))}
        </fieldset>
      ))}
    </div>
  );
}

export default function ProjectAnalysisProfileTab({
  projectId,
  projectName,
  clientName,
  clientId,
  activeProjectFile,
  isActive,
  flash,
}: ProjectAnalysisProfileTabProps) {
  const [vendorTab, setVendorTab] = useState<VendorTab>('moodys');
  const [config, setConfig] = useState<BarConfigDocument | null>(null);
  const [baselineJson, setBaselineJson] = useState('');
  const [busy, setBusy] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const buildPrefill = useCallback(() => {
    const active = activeProjectFile;
    const datasetID = active && active.projectFileID > 0 ? String(active.projectFileID) : '';
    return {
      projectName: (projectName ?? '').trim(),
      clientName: (clientName ?? '').trim(),
      clientId: clientId > 0 ? clientId : 0,
      datasetID,
      clientDateAsOf: toApiDateValue(active?.dataAsOf) ?? '',
      activeFileName: (active?.originalFileName ?? '').trim(),
    };
  }, [activeProjectFile, clientId, clientName, projectName]);

  const projectGeneral = useMemo(() => {
    const prefill = buildPrefill();
    return {
      ClientName: prefill.clientName,
      datasetID: prefill.datasetID,
      clientDateAsOf: prefill.clientDateAsOf,
    };
  }, [buildPrefill]);

  const loadProfile = useCallback(async () => {
    if (projectId <= 0) {
      return;
    }
    setBusy(true);
    setLoadError(null);
    try {
      const data = await apiGet<{ json: string; fromTemplate?: boolean }>(
        `/api/projects/${projectId}/analysis-profile`,
      );
      const parsed = JSON.parse(data.json) as BarConfigDocument;
      // Always merge project ClientName / datasetID / clientDateAsOf into the saved profile.
      const next = applyBarConfigPrefill(parsed, buildPrefill());
      setConfig(next);
      setBaselineJson(JSON.stringify(next));
    } catch (e) {
      setLoadError(e instanceof ApiError ? e.message : 'Could not load analysis profile.');
      setConfig(null);
    } finally {
      setBusy(false);
    }
  }, [projectId, buildPrefill]);

  useEffect(() => {
    if (!isActive || projectId <= 0) {
      return;
    }
    void loadProfile();
  }, [isActive, projectId, loadProfile]);

  // Keep shared general fields aligned with the project when client / active data changes.
  useEffect(() => {
    setConfig((prev) => {
      if (!prev) {
        return prev;
      }
      const synced = applySharedClientFields(prev, projectGeneral);
      if (
        synced.ClientName === prev.ClientName &&
        synced.datasetID === prev.datasetID &&
        synced.clientDateAsOf === prev.clientDateAsOf
      ) {
        return prev;
      }
      return synced;
    });
  }, [projectGeneral]);

  const rmsEntries = useMemo(
    () => (config?.jsonConfig?.AnalysisConfigs ?? []).filter(isRmsAnalysisEntry),
    [config],
  );
  const airEntries = useMemo(
    () => (config?.jsonConfig?.AnalysisConfigs ?? []).filter(isAirAnalysisEntry),
    [config],
  );

  const isDirty = config !== null && JSON.stringify(config) !== baselineJson;

  const handleSave = async () => {
    if (!config || projectId <= 0) {
      return;
    }
    setBusy(true);
    try {
      // Persist with current project general fields so both models share the same values.
      const toSave = applySharedClientFields(config, projectGeneral);
      const json = JSON.stringify(toSave, null, 2);
      await apiPostJson(`/api/projects/${projectId}/analysis-profile`, { json });
      setConfig(toSave);
      setBaselineJson(json);
      flash('success', 'Analysis profile saved.');
    } catch (e) {
      flash('error', e instanceof ApiError ? e.message : 'Could not save analysis profile.');
    } finally {
      setBusy(false);
    }
  };

  const handleRevert = () => {
    if (!baselineJson) {
      return;
    }
    try {
      setConfig(JSON.parse(baselineJson) as BarConfigDocument);
    } catch {
      void loadProfile();
    }
  };

  const handleApplyPrefill = () => {
    if (!config) {
      return;
    }
    setConfig(applyBarConfigPrefill(config, buildPrefill()));
    flash('success', 'Project and active data file values applied to the profile.');
  };

  const generalSection = (
    <fieldset className="analysis-profile-section analysis-profile-general">
      <legend>General</legend>
      <p className="field-help">
        From this project — used for both Moodys and Verisk BAR JSON. Client name comes from the project client;
        dataset ID is the Active Project Data file&apos;s <code>ProjectFileID</code>; date as of comes from that
        file&apos;s Data As Of.
      </p>
      <div className="analysis-profile-grid">
        <FieldRow label="ClientName">
          <input value={projectGeneral.ClientName} readOnly aria-readonly title="From project client" />
        </FieldRow>
        <FieldRow
          label="datasetID"
          hint={
            projectGeneral.datasetID
              ? `ProjectFileID ${projectGeneral.datasetID} from Active Project Data.`
              : 'Mark a Project Data file Active to set datasetID (ProjectFileID).'
          }
        >
          <input
            value={projectGeneral.datasetID}
            readOnly
            aria-readonly
            title="dbo.tProjectFile.ProjectFileID of the Active project data file"
          />
        </FieldRow>
        <FieldRow
          label="clientDateAsOf"
          hint={
            projectGeneral.clientDateAsOf
              ? 'From active Project Data “Data As Of”.'
              : 'Set Data As Of on an Active Project Data file.'
          }
        >
          <input
            type="date"
            value={toInputDateValue(projectGeneral.clientDateAsOf)}
            readOnly
            aria-readonly
            title="From active project data as-of date"
          />
        </FieldRow>
      </div>
    </fieldset>
  );

  if (loadError) {
    return (
      <div className="analysis-profile-tab">
        {generalSection}
        <p className="field-help" role="alert">
          {loadError}
        </p>
      </div>
    );
  }

  if (!config) {
    return (
      <div className="analysis-profile-tab">
        {generalSection}
        <p className="field-help">{busy ? 'Loading analysis profile…' : 'No analysis profile loaded.'}</p>
      </div>
    );
  }

  const active = activeProjectFile;

  return (
    <div className="analysis-profile-tab">
      <div className="analysis-profile-prefill-banner">
        <p>
          <strong>Active project data:</strong>{' '}
          {active
            ? `${active.originalFileName} · version ${active.versionLabel || '—'} · data as of ${formatCalendarDateDisplay(active.dataAsOf)}`
            : 'No file marked Active on Project Data.'}
        </p>
        <button type="button" className="secondary-button" disabled={busy} onClick={handleApplyPrefill}>
          Re-apply project prefill
        </button>
      </div>

      {generalSection}

      <div className="analysis-profile-vendor-tabs" role="tablist" aria-label="Modeling vendor">
        <button
          type="button"
          role="tab"
          className={vendorTab === 'moodys' ? 'active' : ''}
          aria-selected={vendorTab === 'moodys'}
          onClick={() => setVendorTab('moodys')}
        >
          Moodys
        </button>
        <button
          type="button"
          role="tab"
          className={vendorTab === 'verisk' ? 'active' : ''}
          aria-selected={vendorTab === 'verisk'}
          onClick={() => setVendorTab('verisk')}
        >
          Verisk
        </button>
      </div>

      <div className="analysis-profile-vendor-panel">
        {vendorTab === 'moodys' ? (
          <MoodysForm config={config} onChange={setConfig} rmsEntries={rmsEntries} />
        ) : (
          <VeriskForm config={config} onChange={setConfig} airEntries={airEntries} />
        )}
      </div>

      <div className="project-tab-actions">
        <button type="button" className="primary-button" disabled={busy || !isDirty} onClick={() => void handleSave()}>
          {busy ? 'Saving…' : 'Save'}
        </button>
        <button type="button" className="secondary-button" disabled={busy || !isDirty} onClick={handleRevert}>
          Cancel
        </button>
        {isDirty ? <span className="field-help project-tab-dirty-hint">Unsaved changes</span> : null}
      </div>
    </div>
  );
}

function formatCalendarDateDisplay(value: string | null | undefined): string {
  const iso = toInputDateValue(value);
  if (!iso) {
    return '—';
  }
  const [y, m, d] = iso.split('-');
  return `${m}/${d}/${y}`;
}
