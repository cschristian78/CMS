import { ApiError, apiGet, apiPostJson } from './api';

export interface AuthCatUser {
  userID: number;
  userFullName: string;
  email: string;
  mustSetPassword: boolean;
}

export interface AuthSession {
  authenticated: boolean;
  userID?: number;
  email?: string;
  fullName?: string;
  userType?: string;
}

export async function fetchAuthSession(): Promise<AuthSession> {
  try {
    return await apiGet<AuthSession>('/api/auth/me');
  } catch {
    return { authenticated: false };
  }
}

export async function fetchCatLoginUsers(): Promise<AuthCatUser[]> {
  const data = await apiGet<{ users?: AuthCatUser[] }>('/api/auth/cat-users');
  return Array.isArray(data.users) ? data.users : [];
}

export async function loginWithEmail(payload: {
  email: string;
  password?: string;
  newPassword?: string;
}): Promise<{ fullName?: string; email?: string; userID?: number }> {
  return apiPostJson('/api/auth/login', payload);
}

export async function logoutSession(): Promise<void> {
  try {
    await apiPostJson('/api/auth/logout', {});
  } catch (err) {
    if (!(err instanceof ApiError)) {
      throw err;
    }
  }
}
