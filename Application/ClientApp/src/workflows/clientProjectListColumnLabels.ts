/**
 * CMS_PhaseI_All exposes BusinessUnitName and BusinessClassName (description text) via dbo.vProject_Current.
 * Display BU terminology without rewriting row alignment (underlying SQL column name unchanged).
 */
export function clientProjectListColumnHeaderForDisplay(columnName: string): string {
  const compact = compactColumnName(columnName);
  if (compact === 'segmentname') {
    return 'BusinessUnitName';
  }
  if (compact === 'segmentid') {
    return 'BusinessUnitID';
  }
  if (compact === 'statusdescription') {
    return 'Status';
  }
  if (compact === 'revenuetier') {
    return 'Revenue Tier';
  }
  if (compact === 'latestrevenue') {
    return 'Latest Revenue';
  }
  if (compact === 'totalhours') {
    return 'Hours';
  }
  if (compact === 'businessclassname') {
    return 'Business Class';
  }
  if (compact === 'workstatus') {
    return 'Project status';
  }
  return columnName;
}

function compactColumnName(columnName: string): string {
  return columnName.replace(/[\s_\-.]/g, '').toLowerCase();
}

/**
 * Home grid hide-list. Columns remain in dbo.vProject_Current / the list API so
 * dashboards and row actions (Edit uses ProjectID / ClientID) can still use them.
 */
const HOME_LIST_HIDDEN_COLUMNS = new Set([
  'businessunitname',
  'businessunit',
  'buname',
  'businessunitid',
  'segmentname',
  'segmentid',
  'businessclassname',
  'businessclass',
  'bcname',
  'businessclassid',
  'clientid',
  'projectid',
  'projectnumber',
  'projectstatus',
  'latestrevenueid',
  'revenueid',
  'latestrevenueeffectivedate',
  'ugid',
  'usergroupid',
  'usergroupname',
  'ugname',
  'projectrefid',
  'createddate',
  'modifieddate',
  'isactive',
  'projectservices',
]);

export function isHomeProjectListHiddenColumn(columnName: string): boolean {
  const compact = compactColumnName(columnName);
  if (HOME_LIST_HIDDEN_COLUMNS.has(compact)) {
    return true;
  }

  // Pivoted service flags are CategoryName_ServiceName (underscores). Role columns
  // use UserRoleName as-is (typically spaces, no underscore).
  return columnName.includes('_') && !isHomeListKeptUnderscoreColumn(compact);
}

function isHomeListKeptUnderscoreColumn(compact: string): boolean {
  return (
    compact === 'projectonline' ||
    compact === 'isonline' ||
    compact === 'clientname' ||
    compact === 'statusdescription' ||
    compact === 'projectname' ||
    compact === 'ptdescription' ||
    compact === 'renewaldate' ||
    compact === 'duedate' ||
    compact === 'latestrevenue' ||
    compact === 'revenuetier' ||
    compact === 'totalhours' ||
    compact === 'analyticslead'
  );
}
