/**
 * CMS_PhaseI_All exposes BusinessUnitName and BusinessClassName (description text) via dbo.vProject_Current.
 * Display BU terminology without rewriting row alignment (underlying SQL column name unchanged).
 */
export function clientProjectListColumnHeaderForDisplay(columnName: string): string {
  const compact = columnName.replace(/[\s_\-.]/g, '').toLowerCase();
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
  if (compact === 'businessclassname') {
    return 'Business Class';
  }
  return columnName;
}
