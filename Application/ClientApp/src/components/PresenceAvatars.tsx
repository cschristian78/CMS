import { useEffect, useMemo, useRef, useState } from 'react';
import { apiGet, apiPostJson } from '../api';

export interface PresenceUser {
  userID: number;
  fullName: string;
  email: string;
  hasPhoto: boolean;
  lastSeenUtc?: string;
}

interface PresenceSnapshot {
  users?: PresenceUser[];
}

const HEARTBEAT_MS = 25_000;
const MAX_VISIBLE = 5;

const AVATAR_COLORS = [
  '#1a6b8a',
  '#c45c26',
  '#2a7a52',
  '#8a4a6b',
  '#4a5f8a',
  '#a67c2a',
  '#3d6b5c',
  '#7a3d3d',
];

function initialsFor(name: string, email: string): string {
  const trimmed = name.trim();
  if (trimmed) {
    const parts = trimmed.split(/\s+/).filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0][0] ?? ''}${parts[parts.length - 1][0] ?? ''}`.toUpperCase();
    }
    return trimmed.slice(0, 2).toUpperCase();
  }
  const mail = email.trim();
  return mail ? mail.slice(0, 2).toUpperCase() : '?';
}

function colorForUser(userId: number): string {
  const idx = Math.abs(userId) % AVATAR_COLORS.length;
  return AVATAR_COLORS[idx] ?? AVATAR_COLORS[0];
}

function displayName(u: PresenceUser): string {
  return (u.fullName || u.email || `User ${u.userID}`).trim();
}

export default function PresenceAvatars() {
  const [users, setUsers] = useState<PresenceUser[]>([]);
  const [open, setOpen] = useState(false);
  const [brokenPhotos, setBrokenPhotos] = useState<Record<number, boolean>>({});
  const rootRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    let cancelled = false;
    let timer: number | undefined;

    const tick = async () => {
      try {
        const data = await apiPostJson<PresenceSnapshot>('/api/presence/heartbeat', {});
        if (cancelled) {
          return;
        }
        setUsers(Array.isArray(data.users) ? data.users : []);
      } catch {
        if (!cancelled) {
          try {
            const data = await apiGet<PresenceSnapshot>('/api/presence');
            if (!cancelled) {
              setUsers(Array.isArray(data.users) ? data.users : []);
            }
          } catch {
            if (!cancelled) {
              setUsers([]);
            }
          }
        }
      }
    };

    void tick();
    timer = window.setInterval(() => {
      void tick();
    }, HEARTBEAT_MS);

    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        void tick();
      }
    };
    document.addEventListener('visibilitychange', onVisibility);

    return () => {
      cancelled = true;
      if (timer !== undefined) {
        window.clearInterval(timer);
      }
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, []);

  useEffect(() => {
    if (!open) {
      return;
    }
    const onDoc = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDoc);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const visible = useMemo(() => users.slice(0, MAX_VISIBLE), [users]);
  const overflow = Math.max(0, users.length - visible.length);

  if (users.length === 0) {
    return null;
  }

  return (
    <div className="presence-avatars" ref={rootRef}>
      <button
        type="button"
        className="presence-avatars__stack"
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={
          users.length === 1
            ? `${displayName(users[0])} is also in CMS`
            : `${users.length} others are also in CMS`
        }
        title={users.map(displayName).join(', ')}
        onClick={() => setOpen((v) => !v)}
      >
        {visible.map((u, i) => {
          const name = displayName(u);
          const showPhoto = u.hasPhoto && !brokenPhotos[u.userID];
          return (
            <span
              key={u.userID}
              className="presence-avatars__chip"
              style={{ zIndex: visible.length - i, backgroundColor: colorForUser(u.userID) }}
              title={name}
            >
              {showPhoto ? (
                <img
                  src={`/api/admin/users/${u.userID}/photo`}
                  alt=""
                  className="presence-avatars__photo"
                  onError={() => setBrokenPhotos((prev) => ({ ...prev, [u.userID]: true }))}
                />
              ) : (
                <span className="presence-avatars__initials" aria-hidden="true">
                  {initialsFor(u.fullName, u.email)}
                </span>
              )}
            </span>
          );
        })}
        {overflow > 0 ? (
          <span className="presence-avatars__chip presence-avatars__chip--more" style={{ zIndex: 0 }}>
            +{overflow}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="presence-avatars__panel" role="dialog" aria-label="People currently in CMS">
          <p className="presence-avatars__panel-title">Also in CMS</p>
          <ul className="presence-avatars__list">
            {users.map((u) => {
              const name = displayName(u);
              const showPhoto = u.hasPhoto && !brokenPhotos[u.userID];
              return (
                <li key={u.userID} className="presence-avatars__list-item">
                  <span
                    className="presence-avatars__chip presence-avatars__chip--list"
                    style={{ backgroundColor: colorForUser(u.userID) }}
                    aria-hidden="true"
                  >
                    {showPhoto ? (
                      <img
                        src={`/api/admin/users/${u.userID}/photo`}
                        alt=""
                        className="presence-avatars__photo"
                        onError={() => setBrokenPhotos((prev) => ({ ...prev, [u.userID]: true }))}
                      />
                    ) : (
                      <span className="presence-avatars__initials">{initialsFor(u.fullName, u.email)}</span>
                    )}
                  </span>
                  <span className="presence-avatars__list-text">
                    <span className="presence-avatars__list-name">{name}</span>
                    {u.email && u.email !== name ? (
                      <span className="presence-avatars__list-email">{u.email}</span>
                    ) : null}
                  </span>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
