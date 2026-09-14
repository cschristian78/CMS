export type CatAppMode = 'hub' | 'cm' | 'tm';

const STORAGE_KEY = 'catAppMode';

export function getCatAppMode(): CatAppMode {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (raw === 'cm' || raw === 'tm' || raw === 'hub') {
      return raw;
    }
  } catch {
    // ignore
  }
  return 'hub';
}

export function setCatAppMode(mode: CatAppMode): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, mode);
  } catch {
    // ignore
  }
}

/** Resolve sidebar workspace from the current path, syncing mode when entering CM/TM/hub. */
export function resolveCatAppMode(pathname: string): CatAppMode {
  if (pathname === '/') {
    setCatAppMode('hub');
    return 'hub';
  }

  if (pathname === '/tms' || pathname.startsWith('/tms/')) {
    setCatAppMode('tm');
    return 'tm';
  }

  if (
    pathname === '/cms' ||
    pathname.startsWith('/clients') ||
    pathname.startsWith('/projects') ||
    pathname === '/dashboard' ||
    pathname.startsWith('/dashboard/')
  ) {
    setCatAppMode('cm');
    return 'cm';
  }

  // CatAI, Admin, and any other path: keep the last selected workspace.
  return getCatAppMode();
}
