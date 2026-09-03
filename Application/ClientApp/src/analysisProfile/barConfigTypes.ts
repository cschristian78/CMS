/** Shape of GSIC BAR worker config (GSIC_BAR_Config_*.json). */

export interface BarConfigQuestion {
  name: string;
  value: string;
}

export interface BarRmsAnalysisConfig {
  rmsAnalysisProfile: string;
  rmsOutputProfile: string;
  rmsEventRate: string;
  lossTreaties: unknown[];
  rmsAnalysisProfileName: string;
  rmsOutputProfileName: string;
  rmsEventRateName: string;
  model: number;
  modelVersion: string;
}

export interface BarAirAnalysisConfig {
  Earthquake: boolean;
  EarthquakeShake: boolean;
  FireFollowing: boolean;
  SprinklerLeakage: boolean;
  Landslide: boolean;
  Tsunami: boolean;
  Liquefaction: boolean;
  Wind: boolean;
  TropicalCyclone: boolean;
  StormSurge: boolean;
  PrecipitationFlood: boolean;
  SevereStorm: boolean;
  SevereThunderStorm: boolean;
  Hail: boolean;
  StraightLineWinds: boolean;
  Tornado: boolean;
  WinterStorm: boolean;
  OtherPerils: boolean;
  InlandFlood: boolean;
  WildFire: boolean;
  Terrorism: boolean;
  CBRNDomestic: boolean;
  ConventionalDomestic: boolean;
  CoastalFlood: boolean;
  MoveMarineGC: boolean;
  DemandSurge: boolean;
  DemandSurgePct: boolean;
  Disaggregation: boolean;
  StormSurgePercent: string;
  DemandSurgePercent: string;
  LossTreaties: unknown[];
  EventsetPrevious: string;
  EventsetCurrent: string;
  LMFTPreviousId: string;
  LMFTCurrentId: string;
  LMFTPreviousIdName: string;
  LMFTCurrentIdName: string;
  IncludexDS: boolean;
  Model: number;
  ModelVersion: string;
  OnFloodPlains: boolean;
  OffFloodPlains: boolean;
  PrecipitationFloodPercentage: string;
  FZDefault: boolean;
  FZZone1: boolean;
  FZZone2: boolean;
  FZZone3: boolean;
  FZZone4: boolean;
  FZOnFP: boolean;
  FZOffFP: boolean;
  AirExportELT: boolean;
}

export interface BarImportRow {
  Id: number;
  Portfolio: string;
  DisplayName: string;
  Model: number;
  ContractFile: string;
  LocationFile: string;
  ReInsuranceFile: string;
  ModelVersion: string;
  JobId: number;
  TargetExposureDB: string;
  TargetResultDB: string;
  ModelName: string;
  UserPortName: string;
  SaveAsProfile: boolean;
  ImportOnly: boolean;
  Lobs: unknown[];
  Treaties: unknown[];
  AnalysisConfigSet: boolean;
  AnalysisConfig: { Model: number; ModelVersion: string };
  AirAnalysisConfig: BarAirAnalysisConfig | null;
  RmsAnalysisConfig: BarRmsAnalysisConfig | null;
  ContractType: string;
}

export interface BarAnalysisConfigProject {
  ID: number;
  ProjectName: string;
  ClientName: string;
  ClientHeaderName: string;
  ClientId: number;
  Type: string;
  Description: string;
  DateAsOf: string;
  PropertyPlacement: string;
  AnalyticsProfile: string;
  SearchTag: string;
  RMS: boolean;
  AIR: boolean;
  KCC: boolean;
  UseExistingPortfolio: boolean;
  ExistingVendorPortfolio: boolean;
  CreatedBy: string;
  CreatedDT: string;
  UpdatedBy: string;
  UpdatedDT: string;
  IsActive: boolean;
}

export interface BarAnalysisConfigEntry {
  SubmissionID: number;
  Project: BarAnalysisConfigProject;
  Import: BarImportRow[];
  Analysis: unknown[];
  Groups: unknown[];
  Treaties: unknown[];
  SubmittedUser: string;
  UserShortName: string;
  UseExistingPortfolio: boolean;
  SaveAsTemplateProfile: boolean;
}

export interface BarConfigDocument {
  workerTag: string;
  credentialId: string;
  Priority: string;
  questions: BarConfigQuestion[];
  email: string;
  ClientName: string;
  datasetID: string;
  metadata: string;
  fieldVerification: string;
  dataTransformation: string;
  uploadedFiles: string[];
  airAccountFilename: string[];
  airLocationFilename: string[];
  airReinsuranceFilename: string;
  rmsAccountFilename: string[];
  rmsLocationFilename: string[];
  rmsReinsuranceFilename: string;
  clientDateAsOf: string;
  DatabaseSuffix: string | null;
  jsonConfig: {
    submissionType: string;
    tempProjectId: string;
    AnalysisConfigs: BarAnalysisConfigEntry[];
  };
}
