async function refreshCsrf(): Promise<string> {
  const res = await fetch('/api/antiforgery-token', { credentials: 'include' });
  if (!res.ok) {
    throw new Error(`Could not obtain security token (${res.status}).`);
  }
  const data = (await res.json()) as { token: string };
  return data.token;
}

export class ApiError extends Error {
  status: number;
  validationErrors?: Record<string, string[]>;

  constructor(message: string, status: number, validationErrors?: Record<string, string[]>) {
    super(message);
    this.status = status;
    this.validationErrors = validationErrors;
  }

  static async fromResponse(res: Response): Promise<ApiError> {
    const status = res.status;
    const text = await res.text();
    let message = text || res.statusText;
    let validationErrors: Record<string, string[]> | undefined;

    try {
      const json = JSON.parse(text) as {
        title?: string;
        detail?: string;
        errors?: Record<string, string[]>;
      };
      if (json.detail) {
        message = json.detail;
      } else if (json.title) {
        message = json.title;
      }
      if (json.errors && typeof json.errors === 'object') {
        validationErrors = json.errors;
        const parts = Object.entries(json.errors).flatMap(([k, v]) =>
          v.map((x) => `${k}: ${x}`),
        );
        if (parts.length > 0) {
          message = parts.join(' ');
        }
      }
    } catch {
      // keep message as raw text
    }

    return new ApiError(message || `Request failed (${status})`, status, validationErrors);
  }
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, {
    credentials: 'include',
    cache: 'no-store',
  });
  if (!res.ok) {
    throw await ApiError.fromResponse(res);
  }
  const raw = await res.text();
  if (!raw) {
    return undefined as T;
  }
  const contentType = res.headers.get('content-type') ?? '';
  if (!contentType.includes('json') || raw.trimStart().startsWith('<')) {
    throw new ApiError(
      `Expected JSON from ${path}, but received HTML. The API endpoint may be missing on this server — republish/restart the site.`,
      res.status,
    );
  }
  try {
    return JSON.parse(raw) as T;
  } catch {
    throw new ApiError(`Could not parse JSON from ${path}.`, res.status);
  }
}

export async function apiPostJson<T>(path: string, body: unknown): Promise<T> {
  const token = await refreshCsrf();
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'X-XSRF-TOKEN': token,
    },
    body: JSON.stringify(body),
  });

  if (!res.ok) {
    throw await ApiError.fromResponse(res);
  }

  const raw = await res.text();
  if (!raw) {
    return undefined as T;
  }

  return JSON.parse(raw) as T;
}

export async function apiDelete(path: string): Promise<void> {
  const token = await refreshCsrf();
  const res = await fetch(path, {
    method: 'DELETE',
    credentials: 'include',
    headers: {
      'X-XSRF-TOKEN': token,
    },
  });

  if (!res.ok) {
    throw await ApiError.fromResponse(res);
  }
}

export async function apiPostFormData<T>(path: string, formData: FormData): Promise<T> {
  const token = await refreshCsrf();
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'X-XSRF-TOKEN': token,
    },
    body: formData,
  });

  if (!res.ok) {
    throw await ApiError.fromResponse(res);
  }

  const raw = await res.text();
  if (!raw) {
    return undefined as T;
  }

  return JSON.parse(raw) as T;
}
