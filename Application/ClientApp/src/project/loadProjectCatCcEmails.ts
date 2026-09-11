import { apiGet } from '../api';

/** Emails for CAT department members of the project's user group. */
export async function loadProjectCatCcEmails(projectId: number): Promise<string[]> {
  try {
    type CatUserRow = { email?: string | null; Email?: string | null };
    const raw = await apiGet<CatUserRow[]>(`/api/projects/${projectId}/cat-users`);
    const rows = Array.isArray(raw) ? raw : [];
    return [
      ...new Set(rows.map((u) => String(u.email ?? u.Email ?? '').trim()).filter((e) => e.length > 0)),
    ];
  } catch {
    return [];
  }
}
