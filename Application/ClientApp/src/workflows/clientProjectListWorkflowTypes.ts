/**
 * Filter dropdown sources for the home/dashboard client/project listbox.
 * Every list is sourced from `SELECT DISTINCT` against the matching column
 * in `dbo.vProject_Current`:
 *   - businessUnitNames   <- BUName
 *   - businessClassNames  <- BCName
 *   - statusNames         <- ClientStatus
 *   - clientNames         <- ClientName
 *   - projectTypeNames    <- PTDescription
 */
export interface ClientProjectListFilterOptions {
  businessUnitNames: string[];
  businessClassNames: string[];
  statusNames: string[];
  clientNames: string[];
  projectTypeNames: string[];
}
